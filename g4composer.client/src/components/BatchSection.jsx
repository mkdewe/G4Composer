import { useState, useRef, useCallback, useEffect } from 'react'
import styles from './SimpleSection.module.css'
import MolstarViewer from './MolstarViewer.jsx'
import { parseFile } from '../utils/batchParser.js'
import { runQuadro11Stream, fetchSilvaGroups, fetchFrame } from '../services/apiService.js'

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
  const [viewItem,  setViewItem]  = useState(null)  // item currently previewed in Mol* modal
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
        i.id === item.id ? { ...i, status: 'running', error: null, progress: null } : i
      ))
      try {
        const {
          blob, altBlob, stdEnergy, altEnergy, winner,
          jobId, stdFrames, altFrames, stdBestStep, altBestStep,
        } = await runQuadro11Stream(
          [item.input],
          { onProgress: (p) => setItems(prev => prev.map(i =>
              i.id === item.id ? { ...i, progress: p } : i)) }
        )
        setItems(prev => prev.map(i =>
          i.id === item.id ? {
            ...i, status: 'done', progress: null,
            pdbBlob: blob, altBlob: altBlob || null,
            stdEnergy: stdEnergy || null, altEnergy: altEnergy || null,
            winner: winner || 'standard',
            jobId: jobId || null,
            stdFrames: stdFrames || [], altFrames: altFrames || [],
            stdBestStep: stdBestStep ?? null, altBestStep: altBestStep ?? null,
          } : i
        ))
      } catch (err) {
        setItems(prev => prev.map(i =>
          i.id === item.id
            ? { ...i, status: 'error_runtime', progress: null, error: err.details || err.message || 'Unknown error' }
            : i
        ))
        // continue with the next item — resilient mode
      }
    }

    setIsRunning(false)
  }

  // ── Downloads ──────────────────────────────────────────────────────────
  // Single iteration → download that one .pdb. More than one iteration in the input
  // → bundle every iteration's .pdb into a ZIP so the whole minimization trajectory
  // is available, not just the best-energy frame.
  const downloadOne = async (item, variant = 'std') => {
    const frames = variant === 'alt' ? item.altFrames : item.stdFrames
    const name   = sanitiseName(item.name)

    if (item.jobId && Array.isArray(frames) && frames.length > 1) {
      const { default: JSZip } = await import('jszip')
      const zip = new JSZip()
      await addIterationsToZip(zip, item, variant, name)
      const blob = await zip.generateAsync({ type: 'blob' })
      triggerDownload(blob, `${name}_${variant}_iterations.zip`)
      return
    }

    const blob = variant === 'alt' ? item.altBlob : item.pdbBlob
    if (!blob) return
    triggerDownload(blob, `${name}_${variant}.pdb`)
  }

  const downloadAll = async () => {
    const successful = items.filter(i => i.status === 'done' && i.pdbBlob)
    if (successful.length === 0) return

    // Lazy-load JSZip — only fetched if user actually uses bulk download.
    const { default: JSZip } = await import('jszip')
    const zip = new JSZip()
    for (const item of successful) {
      const name = sanitiseName(item.name)
      // Multi-iteration items: fold every iteration into a per-structure folder so the
      // bundle mirrors what the single-item download produces. Otherwise the best frame only.
      if (item.jobId && Array.isArray(item.stdFrames) && item.stdFrames.length > 1) {
        const folder = zip.folder(name)
        await addIterationsToZip(folder, item, 'std', name)
        if (Array.isArray(item.altFrames) && item.altFrames.length > 1)
          await addIterationsToZip(folder, item, 'alt', name)
        continue
      }
      zip.file(`${name}_std.pdb`, await item.pdbBlob.text())
      if (item.altBlob) zip.file(`${name}_alt.pdb`, await item.altBlob.text())
    }
    const blob = await zip.generateAsync({ type: 'blob' })
    triggerDownload(blob, `g4composer_batch_${new Date().toISOString().slice(0, 10)}.zip`)
  }

  // Fetch every iteration frame for one engine variant and add each as a .pdb entry,
  // tagging the best-energy iteration in the filename.
  const addIterationsToZip = async (zip, item, variant, name) => {
    const engine    = variant === 'alt' ? 'alt' : 'std'
    const frames    = variant === 'alt' ? item.altFrames : item.stdFrames
    const bestStep  = variant === 'alt' ? item.altBestStep : item.stdBestStep
    for (const f of frames) {
      const res = await fetchFrame(item.jobId, engine, f.step)
      if (!res?.blob) continue
      const tag = f.step === bestStep ? '_best' : ''
      zip.file(`${name}_${variant}_iter${f.step}${tag}.pdb`, await res.blob.text())
      if (res.url) URL.revokeObjectURL(res.url)
    }
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
                {it.status === 'running' ? (
                  /* While running, Energy + Actions are empty — give the live status their room
                     for the label. Keep the SAME cell count (one wide + two zero-width) so the
                     flex gaps match other rows and nothing shifts. */
                  <>
                  <span style={{ width: 350, minWidth: 0, overflow: 'hidden' }}>
                    <ItemProgress progress={it.progress} />
                  </span>
                  <span style={{ width: 0 }} />
                  <span style={{ width: 0 }} />
                  </>
                ) : (
                  <>
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
                      {it.stdBestStep != null && (it.stdFrames?.length ?? 0) > 1 && (
                        <span style={{ color: 'var(--text-dim)', marginLeft: 4 }} title="Best iteration">@{it.stdBestStep}</span>
                      )}
                    </span>
                  )}
                  {it.altEnergy != null && (
                    <span title="Alternative engine">
                      <span style={{ color: 'var(--text-dim)', marginRight: 3 }}>alt</span>
                      <span style={{ color: it.winner === 'alternative' ? 'var(--teal-dark)' : 'var(--text)', fontWeight: it.winner === 'alternative' ? 700 : 400 }}>
                        {it.altEnergy.toFixed(1)}{it.winner === 'alternative' ? ' ★' : ''}
                      </span>
                      {it.altBestStep != null && (it.altFrames?.length ?? 0) > 1 && (
                        <span style={{ color: 'var(--text-dim)', marginLeft: 4 }} title="Best iteration">@{it.altBestStep}</span>
                      )}
                    </span>
                  )}
                </span>
                <span style={{ width: 110, textAlign: 'right', display: 'flex', gap: 4, justifyContent: 'flex-end', alignItems: 'center' }}>
                  {it.status === 'done' && (
                    <>
                      <button
                        onClick={() => setViewItem(it)}
                        className={`${styles.iconBtn} ${styles.iconBtnView}`}
                        title="Preview in Mol*"
                        aria-label="Preview in Mol*"
                      >
                        <EyeIcon />
                      </button>
                      <button
                        onClick={() => downloadOne(it, 'std')}
                        className={`${styles.iconBtn} ${styles.iconBtnDownload}`}
                        title={(it.stdFrames?.length ?? 0) > 1 ? 'Download standard — all iterations (.zip)' : 'Download standard .pdb'}
                        aria-label="Download standard structure"
                      >
                        <DownloadIcon />
                      </button>
                      {it.altBlob && (
                        <button
                          onClick={() => downloadOne(it, 'alt')}
                          className={`${styles.iconBtn} ${styles.iconBtnDownload}`}
                          title={(it.altFrames?.length ?? 0) > 1 ? 'Download alternative — all iterations (.zip)' : 'Download alternative .pdb'}
                          aria-label="Download alternative structure"
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
                  </>
                )}
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

      {/* ── Overall progress (while running) ── */}
      {isRunning && items.length > 0 && (() => {
        const finished = stats.done + stats.failed
        const pct = items.length ? Math.round((finished / items.length) * 100) : 0
        return (
          <div style={{ margin: '0 0 12px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: 'var(--text-dim)', marginBottom: 6 }}>
              <span>Processing queue…</span>
              <span>{finished}/{items.length} · {pct}%</span>
            </div>
            <div style={{ height: 6, background: 'var(--surface2)', borderRadius: 4, overflow: 'hidden' }}>
              <div style={{
                width: `${pct}%`, height: '100%',
                background: 'linear-gradient(90deg, #2D9AC5, #5DCAA5)',
                borderRadius: 4, transition: 'width 0.4s ease',
              }} />
            </div>
          </div>
        )
      })()}

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

      {/* ── Mol* preview modal ── */}
      {viewItem && (
        <BatchViewerModal item={viewItem} onClose={() => setViewItem(null)} />
      )}
    </div>
  )
}

// ── Mol* preview modal ───────────────────────────────────────────────────────
function BatchViewerModal({ item, onClose }) {
  const [variant, setVariant] = useState(item.winner === 'alternative' ? 'alt' : 'std')
  const [step,    setStep]    = useState(null)   // current iteration step being previewed
  const [pdbUrl,  setPdbUrl]  = useState(null)
  const hasAlt = !!item.altBlob

  const frames   = (variant === 'alt' ? item.altFrames : item.stdFrames) || []
  const bestStep = variant === 'alt' ? item.altBestStep : item.stdBestStep
  const baseBlob = variant === 'alt' ? item.altBlob : item.pdbBlob

  // Object-URL cache keyed by `${engine}_${step}` (frames) or `${engine}_base`
  // (single-iteration best blob). Everything is revoked together on unmount so
  // no URL handed to Mol* is freed while still in use.
  const cacheRef = useRef({})
  useEffect(() => () => {
    for (const url of Object.values(cacheRef.current)) URL.revokeObjectURL(url)
    cacheRef.current = {}
  }, [])

  // Reset to the best-energy iteration whenever the engine variant changes.
  useEffect(() => {
    setStep(bestStep ?? frames[frames.length - 1]?.step ?? null)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- frames/bestStep derive from (item, variant); avoid array-identity re-fires
  }, [variant, item])

  // Resolve the PDB URL for the current (variant, step): fetch the iteration frame
  // when the input carried multiple iterations, otherwise show the single best blob.
  useEffect(() => {
    let cancelled = false
    const engine    = variant === 'alt' ? 'alt' : 'std'
    const useFrames = !!item.jobId && frames.length > 1 && step != null
    const key       = useFrames ? `${engine}_${step}` : `${engine}_base`

    if (cacheRef.current[key]) {
      setPdbUrl(cacheRef.current[key])
      return
    }
    ;(async () => {
      let url = null
      if (useFrames) {
        const res = await fetchFrame(item.jobId, engine, step)
        url = res?.url ?? null
      } else if (baseBlob) {
        url = URL.createObjectURL(baseBlob)
      }
      if (url) cacheRef.current[key] = url
      if (!cancelled) setPdbUrl(url)
    })()
    return () => { cancelled = true }
  }, [variant, step, item, frames.length, baseBlob])

  // Close on Escape.
  useEffect(() => {
    const onKey = e => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const frameEnergy = frames.find(f => f.step === step)?.energy
  const energy = frameEnergy ?? (variant === 'alt' ? item.altEnergy : item.stdEnergy)

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, zIndex: 1000,
        background: 'rgba(15, 23, 30, 0.55)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        padding: 24,
      }}
    >
      <div
        onClick={e => e.stopPropagation()}
        style={{
          width: 'min(960px, 100%)', maxHeight: '90vh',
          display: 'flex', flexDirection: 'column',
          background: 'var(--surface)', border: '1px solid var(--border-med)',
          borderRadius: 'var(--r-lg)', overflow: 'hidden',
          boxShadow: '0 20px 60px rgba(0,0,0,0.35)',
        }}
      >
        {/* Header */}
        <div style={{
          display: 'flex', alignItems: 'center', gap: 12,
          padding: '10px 16px', borderBottom: '1px solid var(--border)',
          background: 'var(--surface2)', flexShrink: 0,
        }}>
          <span style={{ fontFamily: 'var(--mono)', fontSize: 13, fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {item.name}
          </span>
          {/* Engine toggle (only when an alternative model exists) */}
          {hasAlt && (
            <div style={{ display: 'flex', borderRadius: 'var(--r-sm)', overflow: 'hidden', border: '1px solid var(--border-med)' }}>
              {[['std', 'Standard'], ['alt', 'Alternative']].map(([v, label]) => (
                <button
                  key={v}
                  onClick={() => setVariant(v)}
                  style={{
                    padding: '4px 12px', fontSize: 12, fontFamily: 'var(--sans)',
                    border: 'none', cursor: 'pointer',
                    background: variant === v ? 'var(--teal)' : 'var(--surface)',
                    color: variant === v ? '#fff' : 'var(--text-dim)',
                    transition: 'background 0.12s',
                  }}
                >{label}</button>
              ))}
            </div>
          )}
          {frames.length > 1 && step != null && (
            <span style={{ fontFamily: 'var(--mono)', fontSize: 12, color: 'var(--text-dim)' }}>
              iter {step}{step === bestStep ? ' ★' : ''}
            </span>
          )}
          {energy != null && (
            <span style={{ fontFamily: 'var(--mono)', fontSize: 12, color: 'var(--text-dim)' }}>
              Energy {energy.toFixed(1)}
            </span>
          )}
          <button
            onClick={onClose}
            title="Close"
            aria-label="Close"
            style={{
              marginLeft: 'auto', width: 28, height: 28, borderRadius: 'var(--r-sm)',
              border: '1px solid var(--border-med)', background: 'var(--surface)',
              cursor: 'pointer', fontSize: 16, lineHeight: 1, color: 'var(--text-dim)',
            }}
          >×</button>
        </div>

        {/* Iteration slider — only when the input carried more than one CYANA iteration.
            Lets the user scrub every checkpoint frame; ★ marks the best-energy one.
            Same markup/styling as Build G4 & Home so the scrubber looks identical. */}
        {frames.length > 1 && step != null && (
          <div style={{ borderBottom: '1px solid var(--border)', background: 'var(--surface2)', flexShrink: 0 }}>
            <IterationSlider frames={frames} activeStep={step} bestStep={bestStep} onStep={setStep} />
          </div>
        )}

        {/* Viewer — explicit height so Mol* initialises with a real canvas size
            (a flex-derived height can resolve to 0 at init → blank viewer). */}
        <div style={{ height: 'min(560px, 70vh)', position: 'relative' }}>
          <MolstarViewer
            pdbUrl={pdbUrl}
            runState={pdbUrl ? 'done' : 'idle'}
            runStatus=""
            structureName={item.name}
            representation="cartoon"
          />
        </div>
      </div>
    </div>
  )
}

// Iteration scrubber — identical markup/styling to Build G4 & Home (styles.iterSlider
// track/thumb, tick labels absolutely positioned under the thumb, ★ marks the best).
function IterationSlider({ frames, activeStep, bestStep, onStep }) {
  if (!frames?.length) return null
  const idx    = Math.max(0, frames.findIndex(f => f.step === activeStep))
  const thumbW = 16
  return (
    <div style={{ padding: '4px 16px 10px' }}>
      <input
        type="range"
        min={0}
        max={frames.length - 1}
        step={1}
        value={idx}
        onChange={e => onStep(frames[+e.target.value].step)}
        className={styles.iterSlider}
      />
      <div style={{ position: 'relative', height: 16, marginTop: 6, fontFamily: 'var(--mono)', fontSize: 10 }}>
        {frames.map((f, i) => {
          const pct      = frames.length > 1 ? i / (frames.length - 1) * 100 : 50
          const offsetPx = thumbW * (0.5 - pct / 100)
          const isCurrent = f.step === activeStep
          const isBest    = f.step === bestStep
          return (
            <span key={f.step} style={{
              position: 'absolute',
              left: `calc(${pct}% + ${offsetPx}px)`,
              transform: 'translateX(-50%)',
              color: isCurrent ? 'var(--teal-dark)' : 'var(--text-dim)',
              fontWeight: isCurrent ? 700 : 400,
              whiteSpace: 'nowrap',
            }}>
              {f.step}{isBest ? '★' : ''}
            </span>
          )
        })}
      </div>
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

function EyeIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"
         strokeLinecap="round" strokeLinejoin="round">
      <path d="M1 8s2.5-4.5 7-4.5S15 8 15 8s-2.5 4.5-7 4.5S1 8 1 8z" />
      <circle cx="8" cy="8" r="2" />
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

// Advanced per-item status shown in the Status column while a job runs — a real stage label
// plus a percent bar, replacing the plain "running…" badge.
function ItemProgress({ progress }) {
  const pct = progress?.percent != null
    ? Math.round(Math.min(Math.max(progress.percent, 0), 100))
    : null
  return (
    <span style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: 3, alignItems: 'flex-start' }}
          title={progress?.label || 'Running…'}>
      <span style={{ fontSize: 11, color: 'var(--text)', lineHeight: 1.25, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: '100%' }}>
        {progress?.label ? progress.label.replace(/\s*\(.*\)$/, '') : 'Running…'}{pct != null ? ` · ${pct}%` : ''}
      </span>
      <span style={{ height: 5, width: '100%', maxWidth: 200, background: 'var(--surface2)', borderRadius: 3, overflow: 'hidden' }}>
        <span style={{
          display: 'block', height: '100%',
          width: pct != null ? `${pct}%` : '40%',
          background: 'linear-gradient(90deg, #2D9AC5, #5DCAA5)',
          borderRadius: 3, transition: 'width 0.4s ease',
          ...(pct == null ? { animation: 'batchIndeterminate 1.1s ease-in-out infinite' } : {}),
        }} />
      </span>
      <style>{`@keyframes batchIndeterminate{0%{transform:translateX(-60%)}100%{transform:translateX(160%)}}`}</style>
    </span>
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

function triggerDownload(blob, filename) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = filename
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 5000)
}
