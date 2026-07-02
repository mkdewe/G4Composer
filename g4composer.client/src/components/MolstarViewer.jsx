import { useEffect, useRef, useState } from 'react'
import styles from './MolstarViewer.module.css'

// quadro outputs PDB in an Amber-ish style — residue names GUA/ADE/CYT/THY/URA and
// an empty chain-ID column (the strand id lives in the segID, cols 73-76, e.g. STRA).
// Mol* doesn't recognise those as a nucleic polymer, so cartoon comes out empty and
// only ball-and-stick shows. Normalise to standard names + a real chain ID so Mol*
// builds the polymer. Idempotent: already-standard PDBs (DG/G with a chain) are left
// untouched.
const RES_MAP_DNA = { GUA: ' DG', ADE: ' DA', CYT: ' DC', THY: ' DT', URA: '  U' }
const RES_MAP_RNA = { GUA: '  G', ADE: '  A', CYT: '  C', URA: '  U', THY: ' DT' }
function normalisePdb(text) {
  if (!text || !/^(ATOM|HETATM).{13}(GUA|ADE|CYT|THY|URA)/m.test(text)) return text
  const isRna = /\bURA\b/.test(text) || / O2'/.test(text)   // ribose → RNA
  const map = isRna ? RES_MAP_RNA : RES_MAP_DNA
  return text.split('\n').map(line => {
    if (!/^(ATOM|HETATM)/.test(line)) return line
    const l = line.padEnd(80, ' ').split('')
    const res = line.substring(17, 20).trim().toUpperCase()
    const m = map[res]
    if (m) { l[17] = m[0]; l[18] = m[1]; l[19] = m[2] }
    const seg = line.substring(72, 76).trim()      // e.g. STRA → chain A
    if (seg && l[21] === ' ') l[21] = seg[seg.length - 1]
    return l.join('').replace(/\s+$/, '')
  }).join('\n')
}

export default function MolstarViewer({ pdbUrl, runState, runStatus, structureName, representation, progress }) {
  const containerRef = useRef(null)
  const viewerRef    = useRef(null)   // holds the Mol* Viewer instance
  const [molReady, setMolReady] = useState(false)
  const [molError, setMolError] = useState(null)

  // Init Mol* once — using Viewer.create() API (Mol* >= 3.x / 4.x)
  useEffect(() => {
    if (!containerRef.current) return
    let disposed = false

    async function init() {
      try {
        // window.molstar is the global namespace loaded from CDN
        const mol = window.molstar
        if (!mol) {
          setMolError('Mol* library not loaded — check the CDN <script> tag in index.html.')
          return
        }

        // Mol* 3.x/4.x exposes Viewer.create()
        // Older builds exposed createPluginUI() — we support both for safety
        let viewer
        if (mol.Viewer?.create) {
          viewer = await mol.Viewer.create(containerRef.current, {
            layoutIsExpanded:          false,
            layoutShowControls:        false,
            layoutShowRemoteState:     false,
            layoutShowSequence:        true,
            layoutShowLog:             false,
            layoutShowLeftPanel:       true,
            viewportShowExpand:        true,
            viewportShowSelectionMode: false,
            viewportShowAnimation:     false,
          })
        } else if (mol.createPluginUI) {
          // fallback for older CDN versions
          viewer = await mol.createPluginUI(containerRef.current, {
            layoutIsExpanded:          false,
            layoutShowControls:        false,
            layoutShowRemoteState:     false,
            layoutShowSequence:        true,
            layoutShowLog:             false,
            layoutShowLeftPanel:       true,
            viewportShowExpand:        true,
            viewportShowSelectionMode: false,
            viewportShowAnimation:     false,
          })
        } else {
          // Last resort: log available keys to help diagnose version mismatch
          const keys = Object.keys(mol).join(', ')
          setMolError(`Mol* API not found. Available keys: ${keys}`)
          return
        }

        if (!disposed) {
          viewerRef.current = viewer
          setMolReady(true)
        }
      } catch (err) {
        if (!disposed) setMolError(`Mol* init error: ${err.message}`)
      }
    }

    init()

    return () => {
      disposed = true
      // Viewer.create() returns an object with .plugin or .dispose
      const v = viewerRef.current
      try { v?.plugin?.dispose?.() } catch { /* best-effort teardown */ }
      try { v?.dispose?.() } catch { /* best-effort teardown */ }
      viewerRef.current = null
    }
  }, [])

  // Load PDB when URL or active run changes.
  // Always clears previous structure first so tab switching shows only the
  // currently selected run (not an overlay of multiple structures).
  // Uses builders API for both Viewer.create() and createPluginUI paths
  // because it supports a 'label' option — fixing the blob:https name issue.
  useEffect(() => {
    if (!viewerRef.current || !pdbUrl || !molReady) return

    let cancelled = false

    async function load() {
      try {
        const viewer = viewerRef.current
        // viewer.plugin = PluginContext from Viewer.create()
        // viewer itself  = PluginContext from createPluginUI()
        const plugin = viewer.plugin ?? viewer

        // Clear previous structure — essential for tab switching
        await plugin.clear()
        if (cancelled) return

        // Fetch the PDB text ourselves so we can normalise quadro's non-standard
        // residue/chain naming before Mol* parses it (otherwise cartoon is empty).
        const label   = structureName || 'G4 Structure'
        const rawText = await (await fetch(pdbUrl)).text()
        if (cancelled) return
        const data  = await plugin.builders.data.rawData(
          { data: normalisePdb(rawText), label },
          { state: { isGhost: true } }
        )
        if (cancelled) return

        const traj = await plugin.builders.structure.parseTrajectory(data, 'pdb')
        if (cancelled) return

        if (representation === 'cartoon') {
          // Force cartoon regardless of size. The 'default' hierarchy preset is
          // size-adaptive (small structures → full-atom ball-and-stick), which is
          // the inconsistency. We build the structure and apply the built-in
          // 'polymer-cartoon' REPRESENTATION preset — deterministic, polymer-only.
          const model     = await plugin.builders.structure.createModel(traj)
          if (cancelled) return
          const structure = await plugin.builders.structure.createStructure(model)
          if (cancelled) return
          await plugin.builders.structure.representation.applyPreset(structure, 'polymer-cartoon')
        } else {
          await plugin.builders.structure.hierarchy.applyPreset(traj, 'default')
        }
        if (!cancelled) plugin.canvas3d?.requestCameraReset?.({ durationMs: 250 })
      } catch (err) {
        if (!cancelled) console.error('Mol* load error:', err)
      }
    }

    load()
    return () => { cancelled = true }
  }, [pdbUrl, molReady, structureName, representation])

  const loaded  = pdbUrl && molReady && !molError
  const loading = runState === 'running'
  const hasError = (runState === 'error') || !!molError

  return (
    <div className={styles.root}>
      {/* Mol* container — always in DOM so the viewer initialises once */}
      <div
        ref={containerRef}
        className={styles.molContainer}
        style={{ visibility: loaded ? 'visible' : 'hidden' }}
      />

      {/* Loading overlay — current stage label + a real percent bar (no fake timer, no
          growing checklist: phases differ in length and Home can have many, so we show only
          the stage in flight, weighted by percent). */}
      {loading && !loaded && (() => {
        const pct = progress?.percent != null
          ? Math.round(Math.min(Math.max(progress.percent, 0), 100))
          : progress?.total > 0
            ? Math.round(Math.min(Math.max(progress.index ?? 0, 0), progress.total) / progress.total * 100)
            : null
        return (
          <div className={styles.overlay}>
            <div className={styles.center}>
              <div className={styles.rings}>
                <div className={`${styles.ring} ${styles.ring1}`} />
                <div className={`${styles.ring} ${styles.ring2}`} />
                <div className={`${styles.ring} ${styles.ring3}`} />
              </div>
              <p className={styles.loadTitle}>{progress?.label || 'Working…'}</p>
              {progress?.detail && <p className={styles.loadStep}>{progress.detail}</p>}

              {pct != null && (
                <>
                  <div style={{
                    width: 260, maxWidth: '72%', height: 6, marginTop: 14,
                    background: 'rgba(255,255,255,0.12)', borderRadius: 4, overflow: 'hidden',
                  }}>
                    <div style={{
                      width: `${pct}%`, height: '100%',
                      background: 'linear-gradient(90deg, #2D9AC5, #5DCAA5)',
                      borderRadius: 4, transition: 'width 0.4s ease',
                    }} />
                  </div>
                  <p className={styles.loadStep} style={{ marginTop: 6 }}>{pct}%</p>
                </>
              )}
            </div>
          </div>
        )
      })()}

      {/* Idle — no run yet */}
      {!loading && !hasError && !loaded && (
        <div className={styles.overlay}>
          <div className={styles.center} style={{ opacity: 0.45 }}>
            <svg width="48" height="48" viewBox="0 0 48 48" fill="none" style={{ marginBottom: 14 }}>
              <polygon points="24,6 38,14 38,30 24,38 10,30 10,14"
                stroke="var(--teal)" strokeWidth="1" fill="none" opacity=".5"/>
              <polygon points="24,10 35,16.5 24,23 13,16.5" fill="url(#mv1)" opacity=".8"/>
              <polygon points="24,15 35,21.5 24,28 13,21.5" fill="url(#mv2)" opacity=".7"/>
              <polygon points="24,20 35,26.5 24,33 13,26.5" fill="url(#mv3)" opacity=".6"/>
              <defs>
                <linearGradient id="mv1" x1="13" y1="10" x2="35" y2="23" gradientUnits="userSpaceOnUse">
                  <stop stopColor="#2D9AC5"/><stop offset="1" stopColor="#5DCAA5"/>
                </linearGradient>
                <linearGradient id="mv2" x1="13" y1="15" x2="35" y2="28" gradientUnits="userSpaceOnUse">
                  <stop stopColor="#1D7FA5"/><stop offset="1" stopColor="#3DBAA0"/>
                </linearGradient>
                <linearGradient id="mv3" x1="13" y1="20" x2="35" y2="33" gradientUnits="userSpaceOnUse">
                  <stop stopColor="#185F95"/><stop offset="1" stopColor="#2D9A90"/>
                </linearGradient>
              </defs>
            </svg>
            <p style={{ fontSize: 13, color: 'var(--text-dim)' }}>Submit a structure to view the 3D model</p>
          </div>
        </div>
      )}

      {/* Error overlay */}
      {hasError && !loading && (
        <div className={styles.overlay}>
          <div className={styles.center}>
            <svg width="40" height="40" viewBox="0 0 40 40" fill="none" style={{ marginBottom: 14, opacity: .7 }}>
              <circle cx="20" cy="20" r="18" stroke="#EF4444" strokeWidth="1.5"/>
              <line x1="20" y1="12" x2="20" y2="22" stroke="#EF4444" strokeWidth="2"/>
              <circle cx="20" cy="27" r="1.5" fill="#EF4444"/>
            </svg>
            <p style={{ fontSize: 14, fontWeight: 600, color: '#991B1B', marginBottom: 6 }}>
              {molError ? 'Mol* error' : 'Computation error'}
            </p>
            <p style={{ fontSize: 13, color: '#777', lineHeight: 1.6, maxWidth: 340, textAlign: 'center' }}>
              {molError || runStatus}
            </p>
            {molError && molError.includes('Available keys') && (
              <details style={{ marginTop: 14, textAlign: 'left' }}>
                <summary style={{ fontSize: 12, color: 'var(--teal)', cursor: 'pointer' }}>Debug info</summary>
                <pre style={{
                  marginTop: 8, fontFamily: 'var(--mono)', fontSize: 11,
                  color: '#9FE1CB', background: '#1a1d23',
                  padding: '10px 14px', borderRadius: 7,
                  whiteSpace: 'pre-wrap',
                }}>{molError}</pre>
              </details>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
