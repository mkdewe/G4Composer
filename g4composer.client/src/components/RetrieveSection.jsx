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
  const [pdbUrl,     setPdbUrl]     = useState(null)
  const [pdbBlob,    setPdbBlob]    = useState(null)
  const [frameLoading, setFrameLoading] = useState(false)

  // Revoke the previous object URL whenever it's replaced, and on unmount.
  useEffect(() => () => { if (pdbUrl) URL.revokeObjectURL(pdbUrl) }, [pdbUrl])

  const loadFrame = useCallback(async (entryId, step) => {
    setFrameLoading(true)
    const res = await fetchCacheFrame(entryId, step)
    setPdbUrl(res?.url ?? null)
    setPdbBlob(res?.blob ?? null)
    setActiveStep(step)
    setFrameLoading(false)
  }, [])

  // Downloads exactly the iteration currently on screen — each one is its own model
  // under 14N (its own build-up depth), not a snapshot of a shared trajectory.
  const downloadActiveFrame = useCallback(() => {
    if (!pdbBlob || !entry || activeStep == null) return
    const base = (entry.pdbId || `result-${entry.id}`).replace(/[^a-z0-9._-]/gi, '_').slice(0, 80)
    const a = document.createElement('a')
    a.href = URL.createObjectURL(pdbBlob)
    a.download = `${base}_${activeStep}it.pdb`
    a.click()
    setTimeout(() => URL.revokeObjectURL(a.href), 5000)
  }, [pdbBlob, entry, activeStep])

  const handleRetrieve = useCallback(async () => {
    const trimmed = query.trim()
    if (!trimmed || loading) return

    setLoading(true)
    setError('')
    setEntry(null)
    setActiveStep(null)
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
    if (result.frames.length > 0) {
      const best = result.frames.reduce((a, b) =>
        b.etotal != null && (a.etotal == null || b.etotal < a.etotal) ? b : a)
      await loadFrame(result.id, best.step)
    }
  }, [query, loading, loadFrame])

  return (
    <div>
      <h2 className={styles.heading}>Retrieve a previous result</h2>
      <p className={styles.sub}>
        Look up a cached model by its result ID or by PDB ID — not by name, which isn't a
        reliable key. Curated examples keep every iteration checkpoint; ad-hoc runs keep only
        their best result.
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
            <span
              className={entry.isExample ? styles.badgeOk : styles.badgeErr}
              style={entry.isExample ? {} : { background: 'var(--surface2)', color: 'var(--text-dim)' }}
            >
              {entry.isExample ? 'Curated example' : 'Ad-hoc run'}
            </span>
          </div>
          <p style={{ fontSize: 12, color: 'var(--text-dim)', margin: '4px 0 14px' }}>
            engine {entry.engineVersion} · cached {new Date(entry.createdAtUtc).toLocaleString()}
          </p>

          <div style={{
            display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 14, alignItems: 'center',
          }}>
            {entry.frames.map(f => (
              <button
                key={f.step}
                onClick={() => loadFrame(entry.id, f.step)}
                disabled={frameLoading}
                title={`Show the model built with iteration ${f.step}`}
                style={{
                  padding: '6px 12px', borderRadius: 8, fontSize: 12, fontFamily: 'var(--mono)',
                  cursor: frameLoading ? 'wait' : 'pointer',
                  border: f.step === activeStep ? '1px solid var(--teal)' : '1px solid var(--border)',
                  background: f.step === activeStep ? 'var(--teal-light)' : 'var(--surface2)',
                  color: f.step === activeStep ? '#085041' : 'var(--text-sub)',
                }}
              >
                {f.step} it · {f.etotal != null ? f.etotal.toFixed(1) : '—'}
              </button>
            ))}

            <button
              className={styles.btnGhost}
              onClick={downloadActiveFrame}
              disabled={!pdbBlob || frameLoading || activeStep == null}
              title={activeStep == null
                ? 'Pick an iteration first'
                : `Download the model for iteration ${activeStep} as .pdb`}
              style={{ marginLeft: 'auto' }}
            >
              <DownloadIcon />
              {activeStep == null ? 'Download .pdb' : `Download ${activeStep} it`}
            </button>
          </div>

          <div style={{ height: 440, border: '1px solid var(--border)', borderRadius: 8, overflow: 'hidden', position: 'relative' }}>
            <MolstarViewer
              pdbUrl={pdbUrl}
              runState={pdbUrl ? 'done' : 'running'}
              runStatus={frameLoading ? 'Loading iteration…' : 'Cached result'}
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

const DownloadIcon = () => (
  <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
    <path d="M8 2v8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    <path d="M4.5 7.5 8 11l3.5-3.5" stroke="currentColor" strokeWidth="1.5"
          strokeLinecap="round" strokeLinejoin="round" />
    <path d="M3 13h10" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
  </svg>
)
