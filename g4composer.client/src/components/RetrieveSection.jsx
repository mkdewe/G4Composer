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
  const [frameLoading, setFrameLoading] = useState(false)

  // Revoke the previous object URL whenever it's replaced, and on unmount.
  useEffect(() => () => { if (pdbUrl) URL.revokeObjectURL(pdbUrl) }, [pdbUrl])

  const loadFrame = useCallback(async (entryId, step) => {
    setFrameLoading(true)
    const res = await fetchCacheFrame(entryId, step)
    setPdbUrl(res?.url ?? null)
    setActiveStep(step)
    setFrameLoading(false)
  }, [])

  const handleRetrieve = useCallback(async () => {
    const trimmed = query.trim()
    if (!trimmed || loading) return

    setLoading(true)
    setError('')
    setEntry(null)
    setActiveStep(null)
    setPdbUrl(null)

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

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 14 }}>
            {entry.frames.map(f => (
              <button
                key={f.step}
                onClick={() => loadFrame(entry.id, f.step)}
                disabled={frameLoading}
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
