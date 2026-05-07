import { useState, useRef, useCallback, useEffect } from 'react'
import styles from './SimpleSection.module.css'
import { parseFile } from '../utils/batchParser.js'
import { runQuadro11, fetchSilvaGroups } from '../services/apiService.js'

/**
 * BatchSection — upload multiple .inp / .txt files, run them sequentially,
 * download individual PDB results or all of them as a ZIP bundle.
 *
 * Items go through 4 states:
 *   pending → running → done | error
 *
 * Sequential execution is intentional: quadro14L runs one Docker container per
 * job, and parallelism would compete for the same image and easily hit timeouts.
 */
export default function BatchSection() {
  const [items, setItems]         = useState([])  // [{ id, name, source, input, status, error?, pdbBlob?, altBlob?, stdEnergy?, altEnergy?, winner? }]
  const [silvaData, setSilvaData] = useState(null)  // classification data for topology validation
  const [isRunning, setIsRunning] = useState(false)
  const [globalErr, setGlobalErr] = useState(null)
  const fileInputRef = useRef(null)
  const dropRef      = useRef(null)
  const idCounter    = useRef(0)

  // Load classification data once for topology validation in minimal-format blocks.
  // Ingest waits for this to resolve so files dropped early aren't rejected with
  // a "data not loaded yet" error.
  useEffect(() => {
    let cancelled = false
    fetchSilvaGroups().then(data => {
      if (!cancelled && data) setSilvaData(data)
    })
    return () => { cancelled = true }
  }, [])

  // ── File ingest ─────────────────────────────────────────────────────────
  const ingestFiles = useCallback(async (fileList) => {
    setGlobalErr(null)
    const files = Array.from(fileList).filter(f =>
      /\.(inp|txt|fasta|fa)$/i.test(f.name)
    )
    if (files.length === 0) {
      setGlobalErr('No supported files selected (allowed: .inp .txt .fasta .fa).')
      return
    }

    // Make sure classification data is available before parsing — otherwise the
    // minimal format would always fail with "data not loaded yet".
    let classification = silvaData
    if (!classification) {
      classification = await fetchSilvaGroups()
      if (classification) setSilvaData(classification)
    }

    const newItems = []
    for (const file of files) {
      const parsed = await parseFile(file, classification)
      const multipleBlocks = parsed.length > 1
      for (const p of parsed) {
        idCounter.current += 1
        // Append #N only when a single file produced multiple blocks. Single-block
        // files use the file name (or the parsed structure name) directly.
        const fallbackName = multipleBlocks
          ? `${file.name}#${p.blockIndex + 1}`
          : file.name
        newItems.push({
          id:       idCounter.current,
          name:     p.input?.name || fallbackName,
          source:   file.name,
          input:    p.input,
          status:   p.error ? 'error' : 'pending',
          error:    p.error,
          pdbBlob:  null,
          altBlob:  null,
          stdEnergy: null,
          altEnergy: null,
          winner:   null,
        })
      }
    }
    setItems(prev => [...prev, ...newItems])
  }, [silvaData])

  const onFileChange = (e) => {
    if (e.target.files?.length) ingestFiles(e.target.files)
    e.target.value = '' // allow re-selecting the same file
  }

  const onDragOver = (e) => {
    e.preventDefault()
    dropRef.current?.classList.add(styles.dropZoneActive)
  }
  const onDragLeave = () => dropRef.current?.classList.remove(styles.dropZoneActive)
  const onDrop = (e) => {
    e.preventDefault()
    dropRef.current?.classList.remove(styles.dropZoneActive)
    if (e.dataTransfer.files?.length) ingestFiles(e.dataTransfer.files)
  }

  // ── Item management ────────────────────────────────────────────────────
  const removeItem = (id) => {
    setItems(prev => prev.filter(i => i.id !== id))
  }

  const clearAll = () => {
    setItems([])
    setGlobalErr(null)
  }

  // ── Sequential run ─────────────────────────────────────────────────────
  const runBatch = async () => {
    if (isRunning) return
    const pending = items.filter(i => i.status === 'pending' || i.status === 'error_runtime')
    if (pending.length === 0) {
      setGlobalErr('Nothing to run — all items are already done or invalid.')
      return
    }

    setIsRunning(true)
    setGlobalErr(null)

    for (const item of pending) {
      if (!item.input) continue // pre-parse error — skip
      // Mark running
      setItems(prev => prev.map(i =>
        i.id === item.id ? { ...i, status: 'running', error: null } : i
      ))
      try {
        const { blob, altBlob, stdEnergy, altEnergy, winner } = await runQuadro11([item.input])
        setItems(prev => prev.map(i =>
          i.id === item.id ? { ...i, status: 'done', pdbBlob: blob, altBlob: altBlob || null, stdEnergy: stdEnergy || null, altEnergy: altEnergy || null, winner: winner || 'standard' } : i
        ))
      } catch (err) {
        setItems(prev => prev.map(i =>
          i.id === item.id
            ? { ...i, status: 'error_runtime', error: err.details || err.message || 'Unknown error' }
            : i
        ))
        // continue with the next item — resilient mode
      }
    }

    setIsRunning(false)
  }

  // ── Downloads ──────────────────────────────────────────────────────────
  const downloadOne = (item, variant = 'std') => {
    const blob = variant === 'alt' ? item.altBlob : item.pdbBlob
    if (!blob) return
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `${sanitiseName(item.name)}_${variant}.pdb`
    a.click()
    setTimeout(() => URL.revokeObjectURL(a.href), 5000)
  }

  const downloadAll = async () => {
    const successful = items.filter(i => i.status === 'done' && i.pdbBlob)
    if (successful.length === 0) return

    // Lazy-load JSZip — only fetched if user actually uses bulk download.
    const { default: JSZip } = await import('jszip')
    const zip = new JSZip()
    for (const item of successful) {
      const stdText = await item.pdbBlob.text()
      const name    = sanitiseName(item.name)
      zip.file(`${name}_std.pdb`, stdText)
      if (item.altBlob) {
        const altText = await item.altBlob.text()
        zip.file(`${name}_alt.pdb`, altText)
      }
    }
    const blob = await zip.generateAsync({ type: 'blob' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `g4composer_batch_${new Date().toISOString().slice(0, 10)}.zip`
    a.click()
    setTimeout(() => URL.revokeObjectURL(a.href), 5000)
  }

  // ── Derived state ──────────────────────────────────────────────────────
  const stats = {
    total:   items.length,
    pending: items.filter(i => i.status === 'pending').length,
    running: items.filter(i => i.status === 'running').length,
    done:    items.filter(i => i.status === 'done').length,
    failed:  items.filter(i => i.status === 'error' || i.status === 'error_runtime').length,
  }
  const hasResults = stats.done > 0

  return (
    <div>
      <h2 className={styles.heading}>Batch Processing</h2>
      <p className={styles.sub}>
        Upload one or more <code>.inp</code> or <code>.txt</code> files. Two block formats are supported,
        auto-detected by line count:
        {' '}<strong>minimal (5 lines)</strong> — name / sequence / structure / topology / subtype;
        {' '}<strong>full (11 lines)</strong> — full positional .inp without keys.
        Multiple blocks per file are separated by blank lines. Jobs run sequentially; if one fails, the rest continue.
      </p>

      {/* ── Step 1: Upload ── */}
      <div className={styles.card}>
        <div className={styles.cardTitle}><StepBadge>1</StepBadge>Upload structure files</div>

        <div
          ref={dropRef}
          className={styles.dropZone}
          onClick={() => fileInputRef.current?.click()}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
          style={{ cursor: 'pointer', opacity: silvaData ? 1 : 0.7 }}
          title={silvaData ? '' : 'Loading classification data...'}
        >
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor"
               strokeWidth="1.5" style={{ margin: '0 auto 12px', display: 'block', opacity: .4 }}>
            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/>
            <polyline points="17 8 12 3 7 8"/>
            <line x1="12" y1="3" x2="12" y2="15"/>
          </svg>
          <p>Drop files here or <span className={styles.link}>browse from disk</span></p>
          <small>Accepted formats: .inp · .txt · .fasta · .fa</small>
          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept=".inp,.txt,.fasta,.fa"
            style={{ display: 'none' }}
            onChange={onFileChange}
          />
        </div>

        {globalErr && (
          <div style={{ marginTop: 12, color: 'var(--err-text)', fontSize: 13 }}>{globalErr}</div>
        )}
      </div>

      {/* ── Step 2: Queue & actions ── */}
      {items.length > 0 && (
        <div className={styles.card}>
          <div className={styles.cardTitle}>
            <StepBadge>2</StepBadge>
            Queue ({items.length})
            <span style={{ marginLeft: 12, fontSize: 12, color: 'var(--text-dim)', fontWeight: 'normal' }}>
              {stats.done > 0 && <>· <strong style={{ color: 'var(--ok-text)' }}>{stats.done} done</strong></>}
              {stats.failed > 0 && <>{' '}· <strong style={{ color: 'var(--err-text)' }}>{stats.failed} failed</strong></>}
              {stats.running > 0 && <>{' '}· <strong>{stats.running} running</strong></>}
            </span>
          </div>

          <div className={styles.entryTable}>
            <div className={styles.entryHead}>
              <span style={{ width: 28 }}>#</span>
              <span style={{ flex: 1 }}>Name</span>
              <span style={{ width: 140 }}>Source</span>
              <span style={{ width: 110 }}>Status</span>
              <span style={{ width: 130, fontFamily: 'var(--mono)', fontSize: 11 }}>Energy (Energy)</span>
              <span style={{ width: 110, textAlign: 'right' }}>Actions</span>
            </div>
            {items.map((it, i) => (
              <div key={it.id} className={styles.entryRow} title={it.error || ''}>
                <span style={{ width: 28, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>{i + 1}</span>
                <span style={{ flex: 1, fontFamily: 'var(--mono)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {it.name}
                </span>
                <span style={{ width: 140, fontSize: 12, color: 'var(--text-dim)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {it.source}
                </span>
                <span style={{ width: 110 }}>
                  <StatusBadge status={it.status} />
                </span>
                <span style={{ width: 130, fontFamily: 'var(--mono)', fontSize: 11, display: 'flex', flexDirection: 'column', gap: 2 }}>
                  {it.stdEnergy != null && (
                    <span title="Standard engine">
                      <span style={{ color: 'var(--text-dim)', marginRight: 3 }}>std</span>
                      <span style={{ color: it.winner === 'standard' ? 'var(--teal-dark)' : 'var(--text)', fontWeight: it.winner === 'standard' ? 700 : 400 }}>
                        {it.stdEnergy.toFixed(1)}{it.winner === 'standard' ? ' ★' : ''}
                      </span>
                    </span>
                  )}
                  {it.altEnergy != null && (
                    <span title="Alternative engine">
                      <span style={{ color: 'var(--text-dim)', marginRight: 3 }}>alt</span>
                      <span style={{ color: it.winner === 'alternative' ? 'var(--teal-dark)' : 'var(--text)', fontWeight: it.winner === 'alternative' ? 700 : 400 }}>
                        {it.altEnergy.toFixed(1)}{it.winner === 'alternative' ? ' ★' : ''}
                      </span>
                    </span>
                  )}
                </span>
                <span style={{ width: 110, textAlign: 'right', display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                  {it.status === 'done' && (
                    <>
                      <button
                        onClick={() => downloadOne(it, 'std')}
                        className={`${styles.iconBtn} ${styles.iconBtnDownload}`}
                        title="Download standard .pdb"
                        aria-label="Download standard .pdb"
                      >
                        <DownloadIcon />
                      </button>
                      {it.altBlob && (
                        <button
                          onClick={() => downloadOne(it, 'alt')}
                          className={`${styles.iconBtn} ${styles.iconBtnDownload}`}
                          title="Download alternative .pdb"
                          aria-label="Download alternative .pdb"
                          style={{ opacity: 0.7 }}
                        >
                          <DownloadIcon />
                        </button>
                      )}
                    </>
                  )}
                  {!isRunning && (
                    <button
                      onClick={() => removeItem(it.id)}
                      className={`${styles.iconBtn} ${styles.iconBtnDanger}`}
                      title="Remove from queue"
                      aria-label="Remove from queue"
                    >
                      <TrashIcon />
                    </button>
                  )}
                </span>
              </div>
            ))}
          </div>

          {/* Per-item error details */}
          {items.some(i => i.error) && (
            <details style={{ marginTop: 12, fontSize: 12 }}>
              <summary style={{ cursor: 'pointer', color: 'var(--text-dim)' }}>
                Show error details ({items.filter(i => i.error).length})
              </summary>
              <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
                {items.filter(i => i.error).map(i => (
                  <div key={i.id} style={{
                    padding: 8, background: 'var(--err-bg)', border: '1px solid var(--err-border)',
                    borderRadius: 6, fontFamily: 'var(--mono)', whiteSpace: 'pre-wrap',
                    color: 'var(--err-text)',
                  }}>
                    <strong>{i.name}</strong> — {i.error}
                  </div>
                ))}
              </div>
            </details>
          )}
        </div>
      )}

      {/* ── Action bar ── */}
      {items.length > 0 && (
        <div className={styles.submitArea} style={{ display: 'flex', gap: 12, justifyContent: 'flex-end', alignItems: 'center', flexWrap: 'wrap' }}>
          <button
            onClick={clearAll}
            disabled={isRunning}
            className={styles.btnGhost}
          >
            <TrashIcon />
            Clear all
          </button>
          {hasResults && (
            <button
              onClick={downloadAll}
              disabled={isRunning}
              className={`${styles.btnGhost} ${styles.btnGhostAccent}`}
            >
              <DownloadIcon />
              Download all ({stats.done}) as ZIP
            </button>
          )}
          <button
            onClick={runBatch}
            disabled={isRunning || stats.pending === 0}
            className={styles.btnRun}
          >
            {isRunning
              ? <>Running… ({stats.done + stats.failed}/{items.length})</>
              : <>Run batch ({stats.pending})</>}
          </button>
        </div>
      )}
    </div>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────

function DownloadIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"
         strokeLinecap="round" strokeLinejoin="round">
      <path d="M8 1.5v9.5" />
      <path d="M4.5 7.5L8 11l3.5-3.5" />
      <path d="M2.5 13.5h11" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"
         strokeLinecap="round" strokeLinejoin="round">
      <path d="M2.5 4h11" />
      <path d="M6 4V2.5h4V4" />
      <path d="M3.5 4l.7 9a1 1 0 001 .9h5.6a1 1 0 001-.9l.7-9" />
      <path d="M6.5 7v4M9.5 7v4" />
    </svg>
  )
}

function StepBadge({ children }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      width: 22, height: 22, borderRadius: '50%',
      background: 'var(--teal-light)', color: 'var(--teal-dark)',
      fontSize: 12, fontWeight: 600, flexShrink: 0,
    }}>{children}</span>
  )
}

function StatusBadge({ status }) {
  switch (status) {
    case 'pending':       return <span className={styles.badgePending}>pending</span>
    case 'running':       return <span className={styles.badgeRunning}>running…</span>
    case 'done':          return <span className={styles.badgeOk}>done</span>
    case 'error':         return <span className={styles.badgeErr}>parse error</span>
    case 'error_runtime': return <span className={styles.badgeErr}>run failed</span>
    default:              return <span>{status}</span>
  }
}

function sanitiseName(name) {
  return name.replace(/[^a-z0-9._-]/gi, '_').slice(0, 80)
}
