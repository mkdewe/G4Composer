import { useState, useRef, useCallback, useEffect } from 'react'
import MolstarViewer from './MolstarViewer.jsx'
import { runPipeline, fetchPipelinePdb, fetchPipelineAltPdb, fetchFrame } from '../services/apiService.js'
import { downloadInp } from '../utils/inpSerializer.js'
import styles from './HomeSection.module.css'

export default function HomeSection() {
  const [sequence,      setSequence]      = useState('')
  const [phase,         setPhase]         = useState('idle')   // 'idle' | 'running' | 'done'
  const [viennaResult,  setViennaResult]  = useState(null)     // kept for progress log only
  const [gqrsMotifs,    setGqrsMotifs]    = useState([])       // for tab labels
  const [quadroResults, setQuadroResults] = useState({})       // { 'G4_1': { ... } }
  const [activeTab,     setActiveTab]     = useState(null)
  const [progressLog,   setProgressLog]   = useState([])
  const [onquadroResult, setOnquadroResult] = useState(null)  // null | { running, success, matches, count, error }
  const [resultsMode,   setResultsMode]   = useState('predictor')  // 'predictor' | 'aligner'

  // Per-motif slider positions: { 'G4_1': { std: 80, alt: 60 }, ... }
  const [motifSteps,    setMotifSteps]    = useState({})
  const [displayedPdbUrl, setDisplayedPdbUrl] = useState(null)
  const frameCacheRef = useRef({})

  const abortRef = useRef(null)

  // ── Best energy tab ───────────────────────────────────────────────────────
  const bestTool = Object.entries(quadroResults)
    .filter(([, r]) => r.success)
    .map(([tool, r]) => ({
      tool,
      energy: r.displayVariant === 'alternative' ? r.altEnergy : r.stdEnergy,
    }))
    .filter(x => x.energy != null)
    .sort((a, b) => a.energy - b.energy)[0]?.tool ?? null

  // ── Run pipeline ──────────────────────────────────────────────────────────
  const handleRun = useCallback(async () => {
    const seq = sequence.trim()
    if (!seq) return

    setPhase('running')
    setViennaResult(null)
    setGqrsMotifs([])
    setQuadroResults({})
    setActiveTab(null)
    setProgressLog([])
    setOnquadroResult(null)

    const abort = new AbortController()
    abortRef.current = abort

    try {
      for await (const event of runPipeline(seq)) {
        if (abort.signal.aborted) break

        switch (event.type) {
          case 'start':
            setProgressLog(['Pipeline started'])
            break

          case 'viennarna_done': {
            const { success, structure, energy, error } = event
            setViennaResult({ success, structure, energy, error })
            setProgressLog(prev => [...prev,
              success
                ? `ViennaRNA: structure predicted (${formatEnergy(energy)})`
                : `ViennaRNA: failed — ${error}`,
            ])
            break
          }

          case 'gqrs_done': {
            const { success, motifs, count, error } = event
            if (success && motifs?.length) {
              setGqrsMotifs(motifs)
              setProgressLog(prev => [...prev,
                `gqrs: ${count} G-quadruplex motif${count !== 1 ? 's' : ''} found`,
              ])
            } else {
              setProgressLog(prev => [...prev,
                success ? 'gqrs: no G-quadruplex motifs found' : `gqrs: failed — ${error}`,
              ])
            }
            break
          }

          case 'quadro_start':
            setProgressLog(prev => [...prev, `${event.tool}: running Quadro 3D…`])
            break

          case 'quadro_done': {
            const { tool, motifId, success, jobId, stdEnergy, altEnergy, winner,
                    hasAlt, error, inpContent, quadroOutput, combinedStructure,
                    stdFrames: stdFramesMeta, altFrames: altFramesMeta,
                    stdBestStep, altBestStep } = event
            if (success) {
              // Auto-set slider to best-energy step for this motif
              setMotifSteps(prev => ({
                ...prev,
                [tool]: {
                  std: stdBestStep ?? null,
                  alt: altBestStep ?? null,
                },
              }))

              const fetchBoth = async () => {
                const pdbUrl    = await fetchPipelinePdb(jobId)
                const altPdbUrl = hasAlt ? await fetchPipelineAltPdb(jobId) : null
                return { pdbUrl, altPdbUrl }
              }
              fetchBoth().then(({ pdbUrl, altPdbUrl }) => {
                setQuadroResults(prev => {
                  const next = {
                    ...prev,
                    [tool]: {
                      success: true, motifId, jobId, stdEnergy, altEnergy,
                      winner, hasAlt, pdbUrl, altPdbUrl,
                      stdFrames: stdFramesMeta ?? [],
                      altFrames: altFramesMeta ?? [],
                      stdBestStep: stdBestStep ?? null,
                      altBestStep: altBestStep ?? null,
                      combinedStructure: combinedStructure ?? null,
                      displayVariant: winner ?? 'standard',
                      inpContent, error: null,
                    },
                  }
                  const best = Object.entries(next)
                    .filter(([, r]) => r.success)
                    .map(([t, r]) => ({ t, e: r.winner === 'alternative' ? r.altEnergy : r.stdEnergy }))
                    .filter(x => x.e != null)
                    .sort((a, b) => a.e - b.e)[0]
                  if (best) setActiveTab(best.t)
                  return next
                })
              })
              setProgressLog(prev => [...prev,
                `${tool}: 3D model ready (ΔG ${formatEnergy(winner === 'alternative' ? altEnergy : stdEnergy)})`,
              ])
            } else {
              setQuadroResults(prev => ({
                ...prev,
                [tool]: {
                  success: false, motifId, error: error ?? 'Unknown error',
                  combinedStructure: combinedStructure ?? null,
                  inpContent: inpContent ?? null,
                  quadroOutput: quadroOutput ?? null,
                },
              }))
              setProgressLog(prev => [...prev, `${tool}: Quadro failed — ${error}`])
            }
            setActiveTab(prev => prev ?? tool)
            break
          }

          case 'onquadro_start':
            setOnquadroResult({ running: true, success: false, matches: [], count: 0, error: null })
            setProgressLog(prev => [...prev, 'ONQuadro Aligner: running…'])
            break

          case 'onquadro_done': {
            const { success, matches, count, error } = event
            setOnquadroResult({ running: false, success, matches: matches ?? [], count: count ?? 0, error: error ?? null })
            setProgressLog(prev => [...prev,
              success
                ? `ONQuadro Aligner: ${count} match${count !== 1 ? 'es' : ''} found`
                : `ONQuadro Aligner: failed — ${error}`,
            ])
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

  const handleVariantToggle = useCallback((tool, variant) => {
    setDisplayedPdbUrl(null)
    setQuadroResults(prev => {
      const r = prev[tool]
      if (!r) return prev
      return { ...prev, [tool]: { ...r, displayVariant: variant } }
    })
  }, [])

  // ── Active tab data ───────────────────────────────────────────────────────
  const activeQuadro   = activeTab ? quadroResults[activeTab] : null
  const displayVariant = activeQuadro?.displayVariant ?? 'standard'

  const activeStdFrames = activeQuadro?.stdFrames ?? []
  const activeAltFrames = activeQuadro?.altFrames ?? []
  const activeFrames    = displayVariant === 'alternative' ? activeAltFrames : activeStdFrames
  const activeStep      = motifSteps[activeTab]?.[displayVariant === 'alternative' ? 'alt' : 'std'] ?? null
  const activeBestStep  = displayVariant === 'alternative'
    ? activeQuadro?.altBestStep : activeQuadro?.stdBestStep

  const stdStepEnergy = activeStdFrames.find(f => f.step === motifSteps[activeTab]?.std)?.energy
    ?? activeQuadro?.stdEnergy ?? null
  const altStepEnergy = activeAltFrames.find(f => f.step === motifSteps[activeTab]?.alt)?.energy
    ?? activeQuadro?.altEnergy ?? null

  // Lazy-fetch frame PDB when tab / variant / step changes
  useEffect(() => {
    if (!activeQuadro?.success || !activeQuadro.jobId) {
      setDisplayedPdbUrl(
        displayVariant === 'alternative'
          ? (activeQuadro?.altPdbUrl ?? activeQuadro?.pdbUrl ?? null)
          : (activeQuadro?.pdbUrl ?? null)
      )
      return
    }
    const engine = displayVariant === 'alternative' ? 'alt' : 'std'
    if (activeFrames.length <= 1 || !activeStep) {
      setDisplayedPdbUrl(
        displayVariant === 'alternative'
          ? (activeQuadro.altPdbUrl ?? activeQuadro.pdbUrl ?? null)
          : (activeQuadro.pdbUrl ?? null)
      )
      return
    }
    const cacheKey = `${activeQuadro.jobId}_${engine}_${activeStep}`
    if (frameCacheRef.current[cacheKey]) {
      setDisplayedPdbUrl(frameCacheRef.current[cacheKey])
      return
    }
    let cancelled = false
    fetchFrame(activeQuadro.jobId, engine, activeStep).then(result => {
      if (!cancelled && result?.url) {
        frameCacheRef.current[cacheKey] = result.url
        setDisplayedPdbUrl(result.url)
      }
    })
    return () => { cancelled = true }
  }, [activeTab, displayVariant, activeStep, activeQuadro?.jobId, activeQuadro?.success])

  const activePdbUrl = displayedPdbUrl
    ?? (displayVariant === 'alternative'
      ? (activeQuadro?.altPdbUrl ?? activeQuadro?.pdbUrl ?? null)
      : (activeQuadro?.pdbUrl ?? null))

  const quadroRunning = activeTab && gqrsMotifs.length > 0 && !quadroResults[activeTab]
  const viewerState   = quadroRunning ? 'running'
    : (activeQuadro?.success && activePdbUrl) ? 'done'
    : activeQuadro && !activeQuadro.success ? 'error'
    : 'idle'

  const visibleTabs = gqrsMotifs.map(m => `G4_${m.id}`)
  const quadroDone  = Object.keys(quadroResults).length
  const totalMotifs = gqrsMotifs.length

  // ── Download handlers ─────────────────────────────────────────────────────
  function handleDownloadInp() {
    if (!activeQuadro?.inpContent) return
    downloadInp(activeQuadro.inpContent, activeTab ?? 'structure')
  }

  function handleDownloadPdb() {
    if (!activePdbUrl) return
    const engine = displayVariant === 'alternative' ? 'alt' : 'std'
    const step   = motifSteps[activeTab]?.[engine] ?? null
    const suffix = step && activeFrames.length > 1 ? `${engine}_${step}` : engine
    const a = document.createElement('a')
    a.href     = activePdbUrl
    a.download = `${activeTab ?? 'structure'}_${suffix}.pdb`
    a.click()
  }

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
            placeholder="e.g. GGGGAAGGGGAAGGGGAAGGGG  (uppercase = RNA, lowercase = DNA)"
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
        <div className={styles.progressArea} style={{ maxWidth: 780, margin: '0 auto 16px' }}>
          {phase === 'running' && (
            <div className={styles.progressBarRow}>
              <div className={styles.progressTrack}>
                <div
                  className={styles.progressFill}
                  style={{ width: totalMotifs > 0 ? `${Math.round((quadroDone / totalMotifs) * 100)}%` : '10%' }}
                />
              </div>
              <span className={styles.progressLabel}>
                {totalMotifs > 0
                  ? `Quadro ${quadroDone}/${totalMotifs} motifs`
                  : (viennaResult ? 'gqrs running…' : 'ViennaRNA running…')}
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

      {/* ── Mode tabs: Predictor / Aligner ─────────────────────────────── */}
      {(visibleTabs.length > 0 || onquadroResult) && (
        <div className={styles.modeBar}>
          <button
            className={`${styles.modeTab} ${resultsMode === 'predictor' ? styles.modeTabActive : ''}`}
            onClick={() => setResultsMode('predictor')}
          >
            G4 Predictor
            {visibleTabs.length > 0 && (
              <span className={styles.modeChip}>{Object.keys(quadroResults).length}/{visibleTabs.length}</span>
            )}
          </button>
          <button
            className={`${styles.modeTab} ${resultsMode === 'aligner' ? styles.modeTabActive : ''}`}
            onClick={() => setResultsMode('aligner')}
          >
            G4 Aligner
            {onquadroResult?.running && <span className={`${styles.tabDot} ${styles.tabDotPulse}`} style={{ marginLeft: 6 }} />}
            {onquadroResult && !onquadroResult.running && (
              <span className={styles.modeChip}>{onquadroResult.count}</span>
            )}
          </button>
        </div>
      )}

      {/* ── Results panel ────────────────────────────────────────────────── */}
      {visibleTabs.length > 0 && resultsMode === 'predictor' && (
        <div className={styles.resultsPanel}>
          {/* Motif tab bar */}
          <div className={styles.tabBar}>
            <div className={styles.tabs}>
              {visibleTabs.map(tool => {
                const motif  = gqrsMotifs.find(m => `G4_${m.id}` === tool)
                const quadro = quadroResults[tool]
                const isBest = tool === bestTool
                const dv     = quadro?.displayVariant ?? quadro?.winner ?? 'standard'
                const energy = quadro?.success
                  ? (dv === 'alternative' ? quadro.altEnergy : quadro.stdEnergy)
                  : null

                return (
                  <button
                    key={tool}
                    className={`${styles.tab} ${activeTab === tool ? styles.tabActive : ''} ${quadro?.success === false ? styles.tabFailed : ''}`}
                    onClick={() => setActiveTab(tool)}
                  >
                    <TabDot quadro={quadro} running={!quadro && phase === 'running'} />
                    <span className={styles.tabName}>
                      G4_{motif?.id} · {motif?.tetrads}T
                    </span>
                    {isBest && <span className={styles.bestStar} title="Best energy">★</span>}
                    {energy != null && (
                      <span className={styles.energyChip}>{formatEnergy(energy)}</span>
                    )}
                    {quadro?.success === false && (
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

              {/* Action bar — downloads */}
              {activeQuadro && (
                <div className={styles.actionBar}>
                  <div className={styles.actionLeft} />
                  <div className={styles.actionRight}>
                    {activeQuadro.inpContent && (
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
                      disabled={!activePdbUrl}
                      onClick={handleDownloadPdb}
                      title={`Download ${displayVariant} PDB`}
                    >
                      ↓ .pdb ({displayVariant === 'alternative' ? 'alt' : 'std'})
                    </button>
                  </div>
                </div>
              )}

              {/* Energy comparison bar + iteration slider */}
              {activeQuadro?.success && (activeQuadro.stdEnergy != null || activeQuadro.altEnergy != null) && (
                <div style={{ borderBottom: '1px solid var(--border)', background: 'var(--surface2)' }}>
                  <div className={styles.energyBar}>
                    <span className={styles.energyBarLabel}>Energy</span>
                    <EnergyBadge
                      label="Standard"
                      energy={stdStepEnergy}
                      isWinner={activeQuadro.winner === 'standard'}
                      isActive={displayVariant === 'standard'}
                      onClick={() => handleVariantToggle(activeTab, 'standard')}
                    />
                    {activeQuadro.altEnergy != null && (
                      <EnergyBadge
                        label="Alternative"
                        energy={altStepEnergy}
                        isWinner={activeQuadro.winner === 'alternative'}
                        isActive={displayVariant === 'alternative'}
                        onClick={() => handleVariantToggle(activeTab, 'alternative')}
                      />
                    )}
                    <span className={styles.energyBarHint}>lower = better minimization</span>
                  </div>
                  {activeFrames.length > 1 && (
                    <IterationSlider
                      frames={activeFrames}
                      activeStep={activeStep}
                      bestStep={activeBestStep}
                      onStep={step => setMotifSteps(prev => ({
                        ...prev,
                        [activeTab]: {
                          ...(prev[activeTab] ?? {}),
                          [displayVariant === 'alternative' ? 'alt' : 'std']: step,
                        },
                      }))}
                    />
                  )}
                </div>
              )}

              {/* Error row */}
              {activeQuadro?.success === false && (
                <div className={styles.metaRow}>
                  <div className={styles.metaError}>
                    3D model failed: {activeQuadro.error}
                  </div>
                </div>
              )}

              {/* Generated .inp */}
              {activeQuadro?.inpContent && (
                <details className={styles.inpDetails}>
                  <summary className={styles.inpSummary}>Generated .inp input</summary>
                  <pre className={styles.inpPre}>{filterInpDisplay(activeQuadro.inpContent)}</pre>
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
              <div className={styles.viewerWrap}>
                <MolstarViewer
                  pdbUrl={activePdbUrl}
                  runState={viewerState}
                  runStatus={activeQuadro?.error ?? ''}
                  structureName={activeTab}
                />
              </div>
            </div>
          )}
        </div>
      )}

      {/* ── Aligner panel ───────────────────────────────────────────────── */}
      {resultsMode === 'aligner' && (
        <div className={styles.resultsPanel}>
          <AlignerPanel result={onquadroResult} />
        </div>
      )}

      {/* ── Idle placeholder ─────────────────────────────────────────────── */}
      {phase === 'idle' && (
        <div className={styles.idlePlaceholder}>
          <G4Icon />
          <p className={styles.idleText}>
            Enter a nucleotide sequence and click <strong>Run Pipeline</strong> to predict
            secondary structure with ViennaRNA, detect G-quadruplex motifs with gqrs,
            and generate 3D G4 models for each motif in parallel.
          </p>
          <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 8 }}>
            Uppercase = RNA (parallel topology) · lowercase = DNA (antiparallel UDUD)
          </p>
        </div>
      )}
    </div>
  )
}

// ── Sub-components ────────────────────────────────────────────────────────────

function TabDot({ quadro, running }) {
  if (running) return <span className={`${styles.tabDot} ${styles.tabDotPulse}`} />
  if (!quadro) return <span className={`${styles.tabDot} ${styles.tabDotGrey}`} />
  if (!quadro.success) return <span className={`${styles.tabDot} ${styles.tabDotRed}`} />
  return <span className={`${styles.tabDot} ${styles.tabDotGreen}`} />
}

function EnergyBadge({ label, energy, isWinner, isActive, onClick }) {
  return (
    <span
      onClick={onClick}
      title="Click to view this model in Mol*"
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 6,
        padding: '3px 10px', borderRadius: 'var(--r-sm)',
        border: `1px solid ${isActive ? 'var(--teal)' : 'var(--border-med)'}`,
        background: isActive ? 'var(--teal-light)' : 'var(--surface)',
        cursor: 'pointer',
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

function AlignerPanel({ result }) {
  if (!result) return (
    <div className={styles.alignerEmpty}>Aligner results will appear here after the pipeline runs.</div>
  )
  if (result.running) return (
    <div className={styles.alignerEmpty}>
      <span className={`${styles.tabDot} ${styles.tabDotPulse}`} style={{ marginRight: 8 }} />
      ONQuadro Aligner running…
    </div>
  )
  if (!result.success) return (
    <div className={styles.alignerEmpty} style={{ color: 'var(--err-text)' }}>
      Aligner failed: {result.error}
    </div>
  )
  if (result.matches.length === 0) return (
    <div className={styles.alignerEmpty}>No matching structures found in the database.</div>
  )
  return (
    <div>
      <div className={styles.alignerHeader}>
        Found <strong>{result.count}</strong> matching structure{result.count !== 1 ? 's' : ''} · sorted by tract distance
      </div>
      <div className={styles.alignerTableWrap}>
        <table className={styles.alignerTable}>
          <thead>
            <tr>
              <th>QRS notation</th>
              <th>Tetrads</th>
              <th>Molecule</th>
              <th title="Lower is better">Tract dist ↑</th>
              <th title="Higher is better">Linker score ↓</th>
              <th>PDB / Files</th>
            </tr>
          </thead>
          <tbody>
            {result.matches.map((m, i) => (
              <tr key={i} className={i % 2 === 0 ? styles.alignerRowEven : ''}>
                <td className={styles.alignerQrs}>{m.qrs}</td>
                <td className={styles.alignerCenter}>{m.tetradCount}</td>
                <td className={styles.alignerCenter}>{m.molecule}</td>
                <td className={styles.alignerCenter}>{m.tractDistance.toFixed(4)}</td>
                <td className={styles.alignerCenter}>{m.linkerScore.toFixed(4)}</td>
                <td className={styles.alignerFiles}>{m.files}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function G4Icon() {
  return (
    <svg width="52" height="52" viewBox="0 0 52 52" fill="none" style={{ marginBottom: 16 }}>
      <circle cx="26" cy="26" r="24" stroke="var(--border)" strokeWidth="1"/>
      <rect x="14" y="14" width="10" height="10" rx="2" fill="url(#g1)" opacity=".9"/>
      <rect x="28" y="14" width="10" height="10" rx="2" fill="url(#g2)" opacity=".9"/>
      <rect x="14" y="28" width="10" height="10" rx="2" fill="url(#g3)" opacity=".9"/>
      <rect x="28" y="28" width="10" height="10" rx="2" fill="url(#g4)" opacity=".9"/>
      <line x1="19" y1="24" x2="19" y2="28" stroke="var(--border-med)" strokeWidth="1.5"/>
      <line x1="33" y1="24" x2="33" y2="28" stroke="var(--border-med)" strokeWidth="1.5"/>
      <line x1="24" y1="19" x2="28" y2="19" stroke="var(--border-med)" strokeWidth="1.5"/>
      <line x1="24" y1="33" x2="28" y2="33" stroke="var(--border-med)" strokeWidth="1.5"/>
      <defs>
        <linearGradient id="g1" x1="14" y1="14" x2="24" y2="24" gradientUnits="userSpaceOnUse"><stop stopColor="#2D9AC5"/><stop offset="1" stopColor="#5DCAA5"/></linearGradient>
        <linearGradient id="g2" x1="28" y1="14" x2="38" y2="24" gradientUnits="userSpaceOnUse"><stop stopColor="#1D7FA5"/><stop offset="1" stopColor="#3DBAA0"/></linearGradient>
        <linearGradient id="g3" x1="14" y1="28" x2="24" y2="38" gradientUnits="userSpaceOnUse"><stop stopColor="#185F95"/><stop offset="1" stopColor="#2D9A90"/></linearGradient>
        <linearGradient id="g4" x1="28" y1="28" x2="38" y2="38" gradientUnits="userSpaceOnUse"><stop stopColor="#0C3F75"/><stop offset="1" stopColor="#1D7575"/></linearGradient>
      </defs>
    </svg>
  )
}

function formatEnergy(e) {
  if (e == null) return '–'
  return `${Number(e).toFixed(1)} kcal/mol`
}

const HIDDEN_INP_KEYS = /^(test|rm_level|iteration)\s/i
function filterInpDisplay(text) {
  if (!text) return text
  return text.split('\n').filter(l => !HIDDEN_INP_KEYS.test(l.trim())).join('\n')
}
