import { useState, useEffect, useRef, useCallback } from 'react'
import SequenceForm from './SequenceForm.jsx'
import MolstarViewer from './MolstarViewer.jsx'
import PdbPreview from './PdbPreview.jsx'
import RunLog from './RunLog.jsx'
import styles from './BuildSection.module.css'
import { downloadInp } from '../utils/inpSerializer.js'
import { fetchFrame } from '../services/apiService.js'

export default function BuildSection({
  runs,
  activeRunId,
  activeRun,
  currentStatus,
  prefill,
  onRun,
  onReset,
  onSelectRun,
  onRemoveRun,
}) {
  const [activeTab,   setActiveTab]   = useState('viewer')
  const [viewEngine,  setViewEngine]  = useState('standard')  // 'standard' | 'alternative'

  // Per-engine active step (iteration slider position)
  const [stdActiveStep, setStdActiveStep] = useState(null)
  const [altActiveStep, setAltActiveStep] = useState(null)

  // Cache of fetched frame URLs: { [jobId_engine_step]: url }
  const frameCacheRef = useRef({})

  // Auto-select the lower-energy model when a run completes.
  // Runs on run-id change (new tab) or state→done transition.
  // Does NOT reset when switching between existing tabs — user's choice is preserved.
  const prevRunIdRef = useRef(null)
  useEffect(() => {
    if (!activeRun) return
    const isNewRun = activeRun.id !== prevRunIdRef.current
    prevRunIdRef.current = activeRun.id
    if (isNewRun || activeRun.state === 'done') {
      if (isNewRun && activeRun.state !== 'done') return
      setViewEngine(activeRun.winner ?? 'standard')
      // Auto-set slider to best-energy step
      setStdActiveStep(activeRun.stdBestStep ?? null)
      setAltActiveStep(activeRun.altBestStep ?? null)
    }
  }, [activeRun?.id, activeRun?.state, activeRun?.winner, activeRun?.stdBestStep, activeRun?.altBestStep])

  const isRunning = activeRun?.state === 'running' ||
    runs.some(r => r.state === 'running')

  // Fetch a frame PDB (with caching) and return its object URL
  const getFrameUrl = useCallback(async (jobId, engine, step) => {
    if (!jobId || !step) return null
    const cacheKey = `${jobId}_${engine}_${step}`
    if (frameCacheRef.current[cacheKey]) return frameCacheRef.current[cacheKey]
    const result = await fetchFrame(jobId, engine, step)
    if (result?.url) frameCacheRef.current[cacheKey] = result.url
    return result?.url ?? null
  }, [])

  // Resolve which PDB URL is currently shown in Mol*
  const [displayedPdbUrl, setDisplayedPdbUrl] = useState(null)

  const stdStep = stdActiveStep
  const altStep = altActiveStep
  const jobId   = activeRun?.jobInfo?.jobId

  useEffect(() => {
    if (!activeRun || activeRun.state !== 'done') {
      setDisplayedPdbUrl(activeRun?.pdbUrl ?? null)
      return
    }
    const engine     = viewEngine === 'alternative' ? 'alt' : 'std'
    const activeStep = viewEngine === 'alternative' ? altStep : stdStep
    const frames     = viewEngine === 'alternative' ? activeRun.altFrames : activeRun.stdFrames

    if (!frames?.length || !activeStep) {
      // Fallback to best-frame PDB already fetched
      setDisplayedPdbUrl(
        viewEngine === 'alternative' ? (activeRun.altUrl ?? null) : (activeRun.pdbUrl ?? null)
      )
      return
    }

    let cancelled = false
    getFrameUrl(jobId, engine, activeStep).then(url => {
      if (!cancelled) setDisplayedPdbUrl(url ?? (viewEngine === 'alternative' ? activeRun.altUrl : activeRun.pdbUrl))
    })
    return () => { cancelled = true }
  }, [activeRun?.id, activeRun?.state, viewEngine, stdStep, altStep, jobId, getFrameUrl])

  // Resolve active energy for display
  const stdFrames     = activeRun?.stdFrames ?? []
  const altFrames     = activeRun?.altFrames ?? []
  const stdFrameEnergy = stdFrames.find(f => f.step === stdActiveStep)?.energy ?? activeRun?.stdEnergy
  const altFrameEnergy = altFrames.find(f => f.step === altActiveStep)?.energy ?? activeRun?.altEnergy

  const activeEngineLabel = viewEngine === 'alternative' ? 'alt' : 'std'
  // Legacy blob for PDB tab / download
  const activePdbBlob = (viewEngine === 'alternative' && activeRun?.altBlob) ? activeRun.altBlob : activeRun?.pdbBlob

  function downloadPdb() {
    if (!activePdbBlob) return
    const engine = viewEngine === 'alternative' ? 'alt' : 'std'
    const step   = viewEngine === 'alternative' ? altActiveStep : stdActiveStep
    const suffix = step ? `${activeEngineLabel}_${step}` : activeEngineLabel
    const a = document.createElement('a')
    a.href = URL.createObjectURL(activePdbBlob)
    a.download = `${sanitiseName(activeRun.name)}_${suffix}.pdb`
    a.click()
  }

  function handleDownloadInp() {
    if (!activeRun?.inpContent) return
    downloadInp(activeRun.inpContent, activeRun.name)
  }

  return (
    <div>
      <h2 className={styles.heading}>Build your G-quadruplex structure</h2>
      <p className={styles.sub}>
        Fill in the fields below to generate a 3D model. Parameters are computed automatically
        from the input; use the Advanced panel to override individual values.
      </p>

      {/* Form */}
      <SequenceForm onRun={onRun} runState={activeRun?.state ?? 'idle'} prefill={prefill} />

      {/* Results panel — shown when there is at least one run */}
      {runs.length > 0 && (
        <div className={styles.viewerPanel}>

          {/* Run tabs (browser-tab style) */}
          <div className={styles.runTabBar}>
            <div className={styles.runTabs}>
              {runs.map(run => (
                <button
                  key={run.id}
                  className={`${styles.runTab} ${run.id === activeRunId ? styles.runTabActive : ''}`}
                  onClick={() => onSelectRun(run.id)}
                  title={run.name}
                >
                  {/* State indicator dot */}
                  <span className={`${styles.runDot} ${
                    run.state === 'running' ? styles.runDotPulse :
                    run.state === 'done'    ? styles.runDotGreen :
                    run.state === 'error'   ? styles.runDotRed   : styles.runDotGrey
                  }`} />
                  <span className={styles.runTabName}>{run.name}</span>
                  <button
                    className={styles.runTabClose}
                    onClick={e => { e.stopPropagation(); onRemoveRun(run.id) }}
                    title="Close"
                  >×</button>
                </button>
              ))}
            </div>
            <button className={styles.resetBtn} onClick={onReset} title="Close all runs">
              Clear all
            </button>
          </div>

          {/* Content tab bar */}
          {activeRun && (
            <>
              <div className={styles.tabBar}>
                <div className={styles.tabs}>
                  {[
                    { id: 'viewer', label: '3D Viewer (Mol*)' },
                    { id: 'pdb',    label: 'PDB source' },
                    { id: 'log',    label: 'Run log' },
                  ].map(({ id, label }) => (
                    <button
                      key={id}
                      className={`${styles.tab} ${activeTab === id ? styles.tabActive : ''}`}
                      onClick={() => setActiveTab(id)}
                    >
                      {label}
                    </button>
                  ))}
                </div>
                <div className={styles.tabActions}>
                  {activeRun.inpContent && (
                    <button
                      className={styles.actionBtn}
                      onClick={handleDownloadInp}
                      title="Download .inp input file"
                    >
                      ↓ .inp
                    </button>
                  )}
                  <button
                    className={`${styles.actionBtn} ${styles.actionPrimary}`}
                    disabled={!activePdbBlob}
                    onClick={downloadPdb}
                    title={`Download ${activeEngineLabel} PDB · Energy ${viewEngine === 'alternative' ? activeRun.altEnergy?.toFixed(1) : activeRun.stdEnergy?.toFixed(1)}`}
                  >
                    ↓ .pdb
                  </button>
                </div>
              </div>

              {/* Energy + iteration slider bar */}
              {(activeRun.stdEnergy != null || activeRun.altEnergy != null) && (
                <div style={{
                  borderBottom: '1px solid var(--border)',
                  background: 'var(--surface2)',
                }}>
                  {/* Engine selector row */}
                  <div style={{
                    display: 'flex', alignItems: 'center', gap: 16,
                    padding: '8px 16px 6px', fontSize: 12, fontFamily: 'var(--mono)',
                  }}>
                    <span style={{ color: 'var(--text-dim)', marginRight: 4 }}>Energy</span>
                    <EnergyBadge
                      label="Standard"
                      energy={stdFrameEnergy}
                      isWinner={activeRun.winner === 'standard'}
                      isActive={viewEngine === 'standard'}
                      onClick={() => setViewEngine('standard')}
                    />
                    {(activeRun.altEnergy != null || altFrames.length > 0) && (
                      <EnergyBadge
                        label="Alternative"
                        energy={altFrameEnergy}
                        isWinner={activeRun.winner === 'alternative'}
                        isActive={viewEngine === 'alternative'}
                        onClick={() => setViewEngine('alternative')}
                      />
                    )}
                    <span style={{ color: 'var(--text-dim)', fontSize: 11, marginLeft: 'auto' }}>
                      lower = better minimization
                    </span>
                  </div>

                  {/* Single iteration slider — tracks active engine */}
                  {(viewEngine === 'standard' ? stdFrames : altFrames).length > 1 && (
                    <IterationSlider
                      frames={viewEngine === 'standard' ? stdFrames : altFrames}
                      activeStep={viewEngine === 'standard' ? stdActiveStep : altActiveStep}
                      bestStep={viewEngine === 'standard' ? activeRun.stdBestStep : activeRun.altBestStep}
                      onStep={step => {
                        if (viewEngine === 'standard') setStdActiveStep(step)
                        else setAltActiveStep(step)
                      }}
                    />
                  )}
                </div>
              )}

              {/* Tab content */}
              <div className={styles.tabBody}>
                {activeTab === 'viewer' && (
                  <MolstarViewer
                    pdbUrl={displayedPdbUrl}
                    runState={activeRun.state}
                    runStatus={activeRun.status}
                    structureName={activeRun.name}
                    representation="cartoon"
                  />
                )}
                {activeTab === 'pdb' && (
                  <PdbPreview pdbBlob={activeRun.pdbBlob} jobInfo={activeRun.jobInfo} />
                )}
                {activeTab === 'log' && (
                  <RunLog
                    runState={activeRun.state}
                    jobInfo={activeRun.jobInfo}
                    runStatus={activeRun.status}
                    dockerLog={activeRun.dockerLog || ''}
                  />
                )}
              </div>

              {/* Status bar */}
              <div className={styles.statusBar}>
                <div className={styles.statusLeft}>
                  <span className={`${styles.dot} ${
                    activeRun.state === 'running' ? styles.dotPulse :
                    activeRun.state === 'done'    ? styles.dotGreen :
                    activeRun.state === 'error'   ? styles.dotRed   : styles.dotGrey
                  }`} />
                  <span className={styles.statusText}>{activeRun.status}</span>
                </div>
                <div className={styles.statusRight}>
                  {activeRun.jobInfo && (
                    <>
                      <StatusItem label="Job"   value={activeRun.jobInfo.jobId} mono />
                      <StatusItem label="Atoms" value={activeRun.jobInfo.atoms} />
                      {activeRun.jobInfo.elapsed && (
                        <StatusItem label="Time" value={`${(+activeRun.jobInfo.elapsed / 1000).toFixed(1)}s`} />
                      )}
                    </>
                  )}
                  <StatusItem label="Backend" value="localhost:7112" />
                </div>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  )
}

function StatusItem({ label, value, mono }) {
  return (
    <span style={{ fontFamily: 'var(--mono)', fontSize: 11, color: '#888' }}>
      <span style={{ color: '#bbb', marginRight: 4 }}>{label}</span>
      <span style={{ color: '#555', fontWeight: mono ? 600 : 400 }}>{value}</span>
    </span>
  )
}

function EnergyBadge({ label, energy, isWinner, isActive, onClick }) {
  return (
    <span
      onClick={onClick}
      title={onClick ? `Click to view this model in Mol*` : undefined}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 6,
        padding: '3px 10px', borderRadius: 'var(--r-sm)',
        border: `1px solid ${isActive ? 'var(--teal)' : 'var(--border-med)'}`,
        background: isActive ? 'var(--teal-light)' : 'var(--surface)',
        cursor: onClick ? 'pointer' : 'default',
        outline: isActive ? '2px solid var(--teal)' : 'none',
        outlineOffset: 1,
        transition: 'background 0.12s, border-color 0.12s',
      }}
    >
      <span style={{ color: 'var(--text-dim)', fontSize: 11 }}>{label}</span>
      <span style={{
        color: isWinner ? 'var(--teal-dark)' : 'var(--text)',
        fontWeight: isWinner ? 700 : 400,
      }}>
        {energy != null ? energy.toFixed(1) : '—'}
      </span>
      {isWinner && <span style={{ fontSize: 10, color: 'var(--teal-dark)' }}>★</span>}
    </span>
  )
}

function IterationSlider({ frames, activeStep, bestStep, onStep }) {
  if (!frames?.length) return null
  const idx    = Math.max(0, frames.findIndex(f => f.step === activeStep))
  const thumbW = 16

  return (
    <div style={{ padding: '4px 16px 10px', borderTop: '1px solid var(--border)' }}>
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

function sanitiseName(name) {
  return (name || 'structure').replace(/[^a-z0-9._-]/gi, '_').slice(0, 80)
}
