import { useState, useCallback, useEffect } from 'react'
import styles from './SimpleSection.module.css'
import MolstarViewer from './MolstarViewer.jsx'
import { fetchCacheEntry, fetchCacheEntryByPdbId, fetchCacheFrame } from '../services/apiService.js'

// Numeric-only input → look up by cache id; anything else → look up by PDB id.
const isNumericId = s => /^\d+$/.test(s)

export function RetrieveSection() {
  const [query,   setQuery]   = useState('')
  const [loading, setLoading] = useState(false)
  const [error,   setError]   = useState('')
  const [entry,   setEntry]   = useState(null)   // PdbCacheEntryDto
  const [activeStep, setActiveStep] = useState(null)
  const [activeVariant, setActiveVariant] = useState('std')
  const [pdbUrl,     setPdbUrl]     = useState(null)
  const [pdbBlob,    setPdbBlob]    = useState(null)
  const [frameLoading, setFrameLoading] = useState(false)

  // Revoke the previous object URL whenever it's replaced, and on unmount.
  useEffect(() => () => { if (pdbUrl) URL.revokeObjectURL(pdbUrl) }, [pdbUrl])

  const loadFrame = useCallback(async (entryId, step, variant = 'std') => {
    setFrameLoading(true)
    const res = await fetchCacheFrame(entryId, step, variant)
    setPdbUrl(res?.url ?? null)
    setPdbBlob(res?.blob ?? null)
    setActiveStep(step)
    setActiveVariant(variant)
    setFrameLoading(false)
  }, [])

  // Pobiera dokładnie ten model, który jest w podglądzie — jeden przycisk, bez wyboru
  // wariantu obok. Wariant i iterację wybiera się klikając klatkę wyżej; ten blob jest już
  // wczytany przez loadFrame, więc pobranie nie odpytuje serwera drugi raz.
  const downloadActive = useCallback(() => {
    if (!entry || !pdbBlob || activeStep == null) return

    const base = (entry.pdbId || `result-${entry.id}`).replace(/[^a-z0-9._-]/gi, '_').slice(0, 80)
    const a = document.createElement('a')
    a.href = URL.createObjectURL(pdbBlob)
    a.download = `${base}_${activeVariant}_${activeStep}it.pdb`
    a.click()
    setTimeout(() => URL.revokeObjectURL(a.href), 5000)
  }, [entry, pdbBlob, activeVariant, activeStep])

  const handleRetrieve = useCallback(async () => {
    const trimmed = query.trim()
    if (!trimmed || loading) return

    setLoading(true)
    setError('')
    setEntry(null)
    setActiveStep(null)
    setActiveVariant('std')
    setPdbUrl(null)
    setPdbBlob(null)

    const result = isNumericId(trimmed)
      ? await fetchCacheEntry(Number(trimmed))
      : await fetchCacheEntryByPdbId(trimmed)

    setLoading(false)

    if (!result) {
      setError(`No cached result found for "${trimmed}".`)
      return
    }

    setEntry(result)
    const std = result.frames.filter(f => f.variant !== 'alt')
    if (std.length > 0) {
      const best = std.reduce((a, b) =>
        b.etotal != null && (a.etotal == null || b.etotal < a.etotal) ? b : a)
      await loadFrame(result.id, best.step, 'std')
    }
  }, [query, loading, loadFrame])

  return (
    <div>
      <h2 className={styles.heading}>Retrieve a previous result</h2>
      <p className={styles.sub}>
        Look up a cached model by its result ID or by PDB ID — not by name, which isn't a
        reliable key. Both engines are kept: <strong>standard</strong> and{' '}
        <strong>alternative</strong>. Curated examples keep every iteration of each; ad-hoc runs
        keep only the best of each.
      </p>

      <div className={styles.card} style={{ maxWidth: 580, margin: '0 auto' }}>
        <div className={styles.cardTitle}>Result ID or PDB ID</div>
        <div className={styles.retrieveRow}>
          <input
            type="text"
            placeholder="e.g. 42 or 7d5f"
            value={query}
            onChange={e => setQuery(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleRetrieve()}
          />
          <button className={styles.retrieveBtn} onClick={handleRetrieve} disabled={loading}>
            {loading ? 'Searching…' : 'Retrieve'}
          </button>
        </div>
        {error && (
          <p style={{ color: 'var(--err-text)', fontSize: 13, marginTop: 10 }}>{error}</p>
        )}
      </div>

      {entry && (
        <div className={styles.card} style={{ maxWidth: 820, margin: '20px auto 0' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div className={styles.cardTitle} style={{ marginBottom: 0 }}>
              Result #{entry.id}{entry.pdbId ? ` · ${entry.pdbId}` : ''}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span
                className={entry.isExample ? styles.badgeOk : styles.badgeErr}
                style={entry.isExample ? {} : { background: 'var(--surface2)', color: 'var(--text-dim)' }}
              >
                {entry.isExample ? 'Curated example' : 'Ad-hoc run'}
              </span>
              <button
                onClick={downloadActive}
                disabled={frameLoading || !pdbBlob || activeStep == null}
                className={`${styles.iconBtn} ${styles.iconBtnDownload}`}
                title={activeStep == null
                  ? 'Select a model first'
                  : `Download the model shown below — ${activeVariant === 'alt' ? 'alternative' : 'standard'}, ${activeStep} it (.pdb)`}
                aria-label="Download the selected model"
              >
                <DownloadIcon />
              </button>
            </div>
          </div>
          <p style={{ fontSize: 12, color: 'var(--text-dim)', margin: '4px 0 14px' }}>
            engine {entry.engineVersion} · cached {new Date(entry.createdAtUtc).toLocaleString()}
          </p>

          {['std', 'alt'].map(variant => {
            const frames = entry.frames.filter(f =>
              variant === 'alt' ? f.variant === 'alt' : f.variant !== 'alt')
            if (frames.length === 0) return null

            const isActiveGroup = activeVariant === variant
            const label = variant === 'alt' ? 'alternative' : 'standard'

            return (
              <div key={variant} style={{
                display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 10, alignItems: 'center',
              }}>
                <span style={{
                  fontSize: 11, fontFamily: 'var(--mono)', color: 'var(--text-dim)',
                  minWidth: 76, textTransform: 'uppercase', letterSpacing: '.04em',
                }}>
                  {label}
                </span>

                {frames.map(f => {
                  const isActive = isActiveGroup && f.step === activeStep
                  return (
                    <button
                      key={`${variant}-${f.step}`}
                      onClick={() => loadFrame(entry.id, f.step, variant)}
                      disabled={frameLoading}
                      title={`Show the ${label} model built with iteration ${f.step}`}
                      style={{
                        padding: '6px 12px', borderRadius: 8, fontSize: 12, fontFamily: 'var(--mono)',
                        cursor: frameLoading ? 'wait' : 'pointer',
                        border: isActive ? '1px solid var(--teal)' : '1px solid var(--border)',
                        background: isActive ? 'var(--teal-light)' : 'var(--surface2)',
                        color: isActive ? '#085041' : 'var(--text-sub)',
                        opacity: variant === 'alt' && !isActive ? 0.85 : 1,
                      }}
                    >
                      {f.step} it · {f.etotal != null ? f.etotal.toFixed(1) : '—'}
                    </button>
                  )
                })}
              </div>
            )
          })}

          <div style={{ height: 440, border: '1px solid var(--border)', borderRadius: 8, overflow: 'hidden', position: 'relative' }}>
            <MolstarViewer
              pdbUrl={pdbUrl}
              runState={pdbUrl ? 'done' : 'running'}
              runStatus={frameLoading
                ? 'Loading iteration…'
                : `${activeVariant === 'alt' ? 'Alternative' : 'Standard'} · ${activeStep ?? '—'} it`}
              structureName={entry.pdbId || `result-${entry.id}`}
              representation="cartoon"
              progress={null}
            />
          </div>
        </div>
      )}
    </div>
  )
}

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
