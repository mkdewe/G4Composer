import { useState, useRef, useCallback, useEffect } from 'react'
import MolstarViewer from './MolstarViewer.jsx'
import { runPipeline, fetchPipelinePdb } from '../services/apiService.js'
import styles from './HomeSection.module.css'

const TOOL_ORDER = ['ViennaRNA', 'LinearFold', 'RNAStructure', 'MXfold2', 'UNAFold', 'SPOT-RNA', 'UFold']

export default function HomeSection() {
  const [sequence,     setSequence]     = useState('')
  const [phase,        setPhase]        = useState('idle')   // 'idle' | 'running' | 'done'
  const [rnaResults,   setRnaResults]   = useState({})       // { toolName: { success, structure, energy, error } }
  const [quadroResults, setQuadroResults] = useState({})     // { toolName: { success, jobId, stdEnergy, altEnergy, winner, hasAlt, pdbUrl, error } }
  const [activeTab,    setActiveTab]    = useState(null)
  const [progressLog,  setProgressLog]  = useState([])
  const [toolCount,    setToolCount]    = useState(0)

  const abortRef = useRef(null)

  // ── Best energy computation ───────────────────────────────────────────────
  const bestTool = Object.entries(quadroResults)
    .filter(([, r]) => r.success)
    .map(([tool, r]) => ({
      tool,
      energy: r.winner === 'alternative' ? r.altEnergy : r.stdEnergy,
    }))
    .filter(x => x.energy != null)
    .sort((a, b) => a.energy - b.energy)[0]?.tool ?? null

  // ── Tabs visible: all tools in order that have at least RNA done ──────────
  const visibleTabs = TOOL_ORDER.filter(t => rnaResults[t] !== undefined)

  // ── Run pipeline ──────────────────────────────────────────────────────────
  const handleRun = useCallback(async () => {
    const seq = sequence.trim()
    if (!seq) return

    // Reset state
    setPhase('running')
    setRnaResults({})
    setQuadroResults({})
    setActiveTab(null)
    setProgressLog([])
    setToolCount(0)

    const abort = new AbortController()
    abortRef.current = abort

    try {
      for await (const event of runPipeline(seq)) {
        if (abort.signal.aborted) break

        switch (event.type) {
          case 'start':
            setToolCount(event.count ?? TOOL_ORDER.length)
            setProgressLog([`Starting pipeline: ${event.count} tools`])
            break

          case 'rna_done': {
            const { tool, success, structure, energy, error } = event
            setRnaResults(prev => ({
              ...prev,
              [tool]: { success, structure, energy: energy ?? null, error: error ?? null },
            }))
            setProgressLog(prev => [
              ...prev,
              success ? `${tool}: RNA structure computed` : `${tool}: RNA failed — ${error}`,
            ])
            // Auto-select first successful tab
            setActiveTab(prev => prev ?? (success ? tool : null))
            break
          }

          case 'quadro_start':
            setProgressLog(prev => [...prev, `${event.tool}: running Quadro 3D…`])
            break

          case 'quadro_skip': {
            const { tool, original } = event
            setQuadroResults(prev => ({
              ...prev,
              [tool]: { success: false, skipped: true, error: `Same structure as ${original} — skipped` },
            }))
            setProgressLog(prev => [...prev, `${tool}: skipped (same structure as ${original})`])
            break
          }

          case 'quadro_done': {
            const { tool, success, jobId, stdEnergy, altEnergy, winner, hasAlt, error, inpContent, quadroOutput } = event
            if (success) {
              // Fetch PDB asynchronously and update state when ready
              fetchPipelinePdb(jobId).then(pdbUrl => {
                setQuadroResults(prev => {
                  const next = {
                    ...prev,
                    [tool]: { success: true, jobId, stdEnergy, altEnergy, winner, hasAlt, pdbUrl, inpContent, error: null },
                  }
                  // Auto-select the tab with best energy so far
                  const bestEntry = Object.entries(next)
                    .filter(([, r]) => r.success)
                    .map(([t, r]) => ({ t, e: r.winner === 'alternative' ? r.altEnergy : r.stdEnergy }))
                    .filter(x => x.e != null)
                    .sort((a, b) => a.e - b.e)[0]
                  if (bestEntry) setActiveTab(bestEntry.t)
                  return next
                })
              })
              setProgressLog(prev => [
                ...prev,
                `${tool}: 3D model ready (ΔG ${formatEnergy(winner === 'alternative' ? altEnergy : stdEnergy)})`,
              ])
            } else {
              setQuadroResults(prev => ({
                ...prev,
                [tool]: { success: false, error: error ?? 'Unknown error', inpContent: inpContent ?? null, quadroOutput: quadroOutput ?? null },
              }))
              setProgressLog(prev => [...prev, `${tool}: Quadro failed — ${error}`])
            }
            break
          }

          case 'complete':
            setPhase('done')
            setProgressLog(prev => [...prev, 'Pipeline complete.'])
            break

          case 'error':
            setPhase('done')
            setProgressLog(prev => [...prev, `Error: ${event.message}`])
            break

          default:
            break
        }
      }
    } catch (err) {
      if (!abort.signal.aborted) {
        setProgressLog(prev => [...prev, `Pipeline error: ${err.message}`])
        setPhase('done')
      }
    }
  }, [sequence])

  const handleStop = useCallback(() => {
    abortRef.current?.abort()
    setPhase('done')
    setProgressLog(prev => [...prev, 'Stopped by user.'])
  }, [])

  // ── Resolve active tab's data ─────────────────────────────────────────────
  const activeRna    = activeTab ? rnaResults[activeTab]    : null
  const activeQuadro = activeTab ? quadroResults[activeTab] : null

  const activePdbUrl = activeQuadro?.pdbUrl ?? null

  // Determine viewer state for MolstarViewer
  const quadroRunning = activeTab && rnaResults[activeTab]?.success && !quadroResults[activeTab]
  const viewerState   = quadroRunning ? 'running'
    : (activeQuadro?.success && activePdbUrl) ? 'done'
    : activeQuadro && !activeQuadro.success ? 'error'
    : 'idle'

  // ── RNA done count for progress bar ──────────────────────────────────────
  const rnaDoneCount    = Object.keys(rnaResults).length
  const quadroDoneCount = Object.keys(quadroResults).length
  const totalTools      = toolCount || TOOL_ORDER.length

  return (
    <div className={styles.root}>
      {/* ── Input row ────────────────────────────────────────────────────── */}
      <div className={styles.inputRow}>
        <div className={styles.seqWrapper}>
          <label className={styles.seqLabel} htmlFor="pipeline-seq">
            G4 Sequence
          </label>
          <input
            id="pipeline-seq"
            className={styles.seqInput}
            type="text"
            value={sequence}
            onChange={e => setSequence(e.target.value)}
            placeholder="e.g. ggggaaaacccc"
            spellCheck={false}
            disabled={phase === 'running'}
            onKeyDown={e => e.key === 'Enter' && phase !== 'running' && handleRun()}
          />
        </div>
        {phase !== 'running' ? (
          <button
            className={styles.runBtn}
            onClick={handleRun}
            disabled={!sequence.trim()}
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
              <polygon points="5,3 19,12 5,21"/>
            </svg>
            Run Pipeline
          </button>
        ) : (
          <button className={`${styles.runBtn} ${styles.stopBtn}`} onClick={handleStop}>
            <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor">
              <rect x="4" y="4" width="16" height="16"/>
            </svg>
            Stop
          </button>
        )}
      </div>

      {/* ── Progress area ────────────────────────────────────────────────── */}
      {(phase === 'running' || (phase === 'done' && progressLog.length > 0)) && (
        <div className={styles.progressArea}>
          {phase === 'running' && (
            <div className={styles.progressBarRow}>
              <div className={styles.progressTrack}>
                <div
                  className={styles.progressFill}
                  style={{ width: `${Math.round((rnaDoneCount / totalTools) * 100)}%` }}
                />
              </div>
              <span className={styles.progressLabel}>
                RNA {rnaDoneCount}/{totalTools}
                {quadroDoneCount > 0 && ` · 3D ${quadroDoneCount}`}
                {progressLog.length > 0 && ` · ${progressLog[progressLog.length - 1]}`}
              </span>
            </div>
          )}
          {phase === 'done' && (
            <div className={styles.progressLog}>
              {progressLog.slice(-6).map((msg, i) => (
                <span key={i} className={styles.logLine}>{msg}</span>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── Results panel ────────────────────────────────────────────────── */}
      {visibleTabs.length > 0 && (
        <div className={styles.resultsPanel}>

          {/* Tab bar */}
          <div className={styles.tabBar}>
            <div className={styles.tabs}>
              {visibleTabs.map(tool => {
                const rna    = rnaResults[tool]
                const quadro = quadroResults[tool]
                const isBest = tool === bestTool
                const energy = quadro?.success
                  ? (quadro.winner === 'alternative' ? quadro.altEnergy : quadro.stdEnergy)
                  : null

                return (
                  <button
                    key={tool}
                    className={`${styles.tab} ${activeTab === tool ? styles.tabActive : ''} ${!rna?.success ? styles.tabFailed : ''}`}
                    onClick={() => setActiveTab(tool)}
                  >
                    <TabDot rna={rna} quadro={quadro} />
                    <span className={styles.tabName}>{tool}</span>
                    {isBest && <span className={styles.bestStar} title="Best energy">★</span>}
                    {energy != null && (
                      <span className={styles.energyChip}>{formatEnergy(energy)}</span>
                    )}
                    {rna?.success === false && (
                      <span className={styles.failChip}>!</span>
                    )}
                  </button>
                )
              })}
            </div>
          </div>

          {/* Tab body */}
          {activeTab && (
            <div className={styles.tabBody}>
              {/* Meta row */}
              <div className={styles.metaRow}>
                {activeRna?.success && (
                  <div className={styles.metaItem}>
                    <span className={styles.metaLabel}>Structure</span>
                    <code className={styles.structureCode}>{activeRna.structure}</code>
                  </div>
                )}
                {activeRna?.success === false && (
                  <div className={styles.metaError}>
                    RNA prediction failed: {activeRna.error}
                  </div>
                )}
                {activeQuadro?.success && (
                  <>
                    <div className={styles.metaItem}>
                      <span className={styles.metaLabel}>ΔG std</span>
                      <span className={styles.metaValue}>{formatEnergy(activeQuadro.stdEnergy)}</span>
                    </div>
                    {activeQuadro.hasAlt && (
                      <div className={styles.metaItem}>
                        <span className={styles.metaLabel}>ΔG alt</span>
                        <span className={styles.metaValue}>{formatEnergy(activeQuadro.altEnergy)}</span>
                      </div>
                    )}
                    <div className={styles.metaItem}>
                      <span className={styles.metaLabel}>Winner</span>
                      <span className={`${styles.metaValue} ${styles.winnerBadge}`}>
                        {activeQuadro.winner}
                      </span>
                    </div>
                    {activeQuadro.inpContent && (
                      <div className={styles.metaItem}>
                        <span className={styles.metaLabel}>Input</span>
                        <InpPopover content={activeQuadro.inpContent} />
                      </div>
                    )}
                  </>
                )}
                {activeQuadro?.success === false && !activeQuadro.skipped && (
                  <div className={styles.metaError}>
                    3D model failed: {activeQuadro.error}
                  </div>
                )}
              </div>

              {/* Generated .inp — shown on both success and failure */}
              {activeQuadro?.inpContent && (
                <details className={styles.inpDetails}>
                  <summary className={styles.inpSummary}>Generated .inp input</summary>
                  <pre className={styles.inpPre}>{activeQuadro.inpContent}</pre>
                </details>
              )}

              {/* Quadro stdout/stderr — only on failure */}
              {activeQuadro?.quadroOutput && (
                <details className={styles.inpDetails} open>
                  <summary className={styles.inpSummary}>Quadro output (stdout / stderr)</summary>
                  <pre className={styles.inpPre}>{activeQuadro.quadroOutput}</pre>
                </details>
              )}


              {/* 3D viewer */}
              {!activeQuadro?.skipped && (
                <div className={styles.viewerWrap}>
                  <MolstarViewer
                    pdbUrl={activePdbUrl}
                    runState={viewerState}
                    runStatus={activeQuadro?.error ?? ''}
                    structureName={activeTab}
                  />
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* ── Idle placeholder ─────────────────────────────────────────────── */}
      {phase === 'idle' && (
        <div className={styles.idlePlaceholder}>
          <PipelineIcon />
          <p className={styles.idleText}>
            Enter a nucleotide sequence and click <strong>Run Pipeline</strong> to predict
            secondary structure with 7 RNA tools and generate 3D G-quadruplex models in parallel.
          </p>
        </div>
      )}
    </div>
  )
}

// ── Sub-components ────────────────────────────────────────────────────────────

function TabDot({ rna, quadro }) {
  if (!rna) return <span className={`${styles.tabDot} ${styles.tabDotGrey}`} />
  if (!rna.success) return <span className={`${styles.tabDot} ${styles.tabDotRed}`} />
  if (!quadro) return <span className={`${styles.tabDot} ${styles.tabDotPulse}`} />
  if (quadro.skipped) return <span className={`${styles.tabDot} ${styles.tabDotSkipped}`} title="Duplicate structure — skipped" />
  if (!quadro.success) return <span className={`${styles.tabDot} ${styles.tabDotRed}`} />
  return <span className={`${styles.tabDot} ${styles.tabDotGreen}`} />
}

function InpPopover({ content }) {
  const [open, setOpen] = useState(false)
  const ref = useRef(null)

  useEffect(() => {
    if (!open) return
    function handleClick(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [open])

  return (
    <div className={styles.inpPopoverWrap} ref={ref}>
      <button
        className={styles.inpIconBtn}
        onClick={() => setOpen(v => !v)}
        title="Show generated .inp parameters"
        aria-expanded={open}
      >ⓘ Show .inp</button>
      {open && (
        <div className={styles.inpPopover}>
          <pre className={styles.inpPopoverPre}>{content}</pre>
        </div>
      )}
    </div>
  )
}

function PipelineIcon() {
  return (
    <svg width="52" height="52" viewBox="0 0 52 52" fill="none" style={{ marginBottom: 16 }}>
      <circle cx="26" cy="26" r="24" stroke="var(--border)" strokeWidth="1"/>
      <polygon points="26,12 36,18 26,24 16,18" fill="url(#pi1)" opacity=".9"/>
      <polygon points="26,17 36,23 26,29 16,23" fill="url(#pi2)" opacity=".8"/>
      <polygon points="26,22 36,28 26,34 16,28" fill="url(#pi3)" opacity=".7"/>
      <polygon points="26,27 36,33 26,39 16,33" fill="url(#pi4)" opacity=".6"/>
      <defs>
        <linearGradient id="pi1" x1="16" y1="12" x2="36" y2="24" gradientUnits="userSpaceOnUse"><stop stopColor="#2D9AC5"/><stop offset="1" stopColor="#5DCAA5"/></linearGradient>
        <linearGradient id="pi2" x1="16" y1="17" x2="36" y2="29" gradientUnits="userSpaceOnUse"><stop stopColor="#1D7FA5"/><stop offset="1" stopColor="#3DBAA0"/></linearGradient>
        <linearGradient id="pi3" x1="16" y1="22" x2="36" y2="34" gradientUnits="userSpaceOnUse"><stop stopColor="#185F95"/><stop offset="1" stopColor="#2D9A90"/></linearGradient>
        <linearGradient id="pi4" x1="16" y1="27" x2="36" y2="39" gradientUnits="userSpaceOnUse"><stop stopColor="#0C3F75"/><stop offset="1" stopColor="#1D7575"/></linearGradient>
      </defs>
    </svg>
  )
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function formatEnergy(e) {
  if (e == null) return '–'
  return `${Number(e).toFixed(1)} kcal/mol`
}
