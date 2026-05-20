import { useState, useRef, useCallback, useEffect } from 'react'
import MolstarViewer from './MolstarViewer.jsx'
import { runPipeline, fetchPipelinePdb, fetchPipelineAltPdb, fetchFrame } from '../services/apiService.js'
import { downloadInp } from '../utils/inpSerializer.js'
import styles from './HomeSection.module.css'

export default function HomeSection() {
  const [sequence,  setSequence]  = useState('')
  const [phase,     setPhase]     = useState('idle')   // 'idle' | 'running' | 'done'
  const [result,    setResult]    = useState(null)     // null | { running, success, ... }
  const [steps,     setSteps]     = useState({ std: null, alt: null })
  const [displayedPdbUrl, setDisplayedPdbUrl] = useState(null)
  const frameCacheRef = useRef({})
  const abortRef      = useRef(null)

  // ── Run pipeline ──────────────────────────────────────────────────────────
  const handleRun = useCallback(async () => {
    const seq = sequence.trim()
    if (!seq) return

    setPhase('running')
    setResult(null)
    setSteps({ std: null, alt: null })
    setDisplayedPdbUrl(null)

    const abort = new AbortController()
    abortRef.current = abort

    try {
      for await (const event of runPipeline(seq)) {
        if (abort.signal.aborted) break

        switch (event.type) {
          case 'aligner_quadro_start':
            setResult({ running: true })
            break

          case 'aligner_quadro_done': {
            const { success, jobId, qrs, rnaStructure, stdEnergy, altEnergy, winner,
                    hasAlt, error, inpContent, quadroOutput, combinedStructure,
                    stdFrames: stdFramesMeta, altFrames: altFramesMeta,
                    stdBestStep, altBestStep,
                    eltetradoOutput, eltetradoError } = event
            if (success) {
              setSteps({ std: stdBestStep ?? null, alt: altBestStep ?? null })
              const fetchBoth = async () => {
                const pdbUrl    = await fetchPipelinePdb(jobId)
                const altPdbUrl = hasAlt ? await fetchPipelineAltPdb(jobId) : null
                return { pdbUrl, altPdbUrl }
              }
              fetchBoth().then(({ pdbUrl, altPdbUrl }) => {
                setResult({
                  running: false, success: true, jobId, qrs,
                  stdEnergy, altEnergy, winner, hasAlt,
                  pdbUrl, altPdbUrl,
                  stdFrames: stdFramesMeta ?? [],
                  altFrames: altFramesMeta ?? [],
                  stdBestStep: stdBestStep ?? null,
                  altBestStep: altBestStep ?? null,
                  combinedStructure: combinedStructure ?? null,
                  displayVariant: winner ?? 'standard',
                  inpContent, error: null,
                  rnaStructure: rnaStructure ?? null,
                  eltetradoOutput: eltetradoOutput ?? null,
                  eltetradoError: eltetradoError ?? null,
                })
              })
            } else {
              setResult({
                running: false, success: false, qrs, error: error ?? 'Unknown error',
                inpContent: inpContent ?? null,
                quadroOutput: quadroOutput ?? null,
                combinedStructure: combinedStructure ?? null,
              })
            }
            break
          }

          case 'complete':
            setPhase('done')
            break

          case 'error':
            setResult({ running: false, success: false, error: event.message })
            setPhase('done')
            break

          default:
            break
        }
      }
    } catch (err) {
      if (!abort.signal.aborted) setPhase('done')
    }
  }, [sequence])

  const handleStop = useCallback(() => {
    abortRef.current?.abort()
    setPhase('done')
  }, [])

  const handleVariantToggle = useCallback((variant) => {
    setDisplayedPdbUrl(null)
    setResult(prev => prev ? { ...prev, displayVariant: variant } : prev)
  }, [])

  // ── Derived state ─────────────────────────────────────────────────────────
  const displayVariant = result?.displayVariant ?? 'standard'
  const stdFrames      = result?.stdFrames ?? []
  const altFrames      = result?.altFrames ?? []
  const frames         = displayVariant === 'alternative' ? altFrames : stdFrames
  const activeStep     = steps[displayVariant === 'alternative' ? 'alt' : 'std'] ?? null
  const bestStep       = displayVariant === 'alternative' ? result?.altBestStep : result?.stdBestStep
  const stdStepEnergy  = stdFrames.find(f => f.step === steps.std)?.energy ?? result?.stdEnergy ?? null
  const altStepEnergy  = altFrames.find(f => f.step === steps.alt)?.energy ?? result?.altEnergy ?? null

  // ── Lazy-fetch frame PDB ──────────────────────────────────────────────────
  useEffect(() => {
    if (!result?.success || !result.jobId) {
      setDisplayedPdbUrl(
        displayVariant === 'alternative'
          ? (result?.altPdbUrl ?? result?.pdbUrl ?? null)
          : (result?.pdbUrl ?? null)
      )
      return
    }
    const engine = displayVariant === 'alternative' ? 'alt' : 'std'
    if (frames.length <= 1 || !activeStep) {
      setDisplayedPdbUrl(
        displayVariant === 'alternative'
          ? (result.altPdbUrl ?? result.pdbUrl ?? null)
          : (result.pdbUrl ?? null)
      )
      return
    }
    const cacheKey = `${result.jobId}_${engine}_${activeStep}`
    if (frameCacheRef.current[cacheKey]) {
      setDisplayedPdbUrl(frameCacheRef.current[cacheKey])
      return
    }
    let cancelled = false
    fetchFrame(result.jobId, engine, activeStep).then(r => {
      if (!cancelled && r?.url) {
        frameCacheRef.current[cacheKey] = r.url
        setDisplayedPdbUrl(r.url)
      }
    })
    return () => { cancelled = true }
  }, [displayVariant, activeStep, result?.jobId, result?.success])

  const activePdbUrl = displayedPdbUrl
    ?? (displayVariant === 'alternative'
      ? (result?.altPdbUrl ?? result?.pdbUrl ?? null)
      : (result?.pdbUrl ?? null))

  const viewerState = result?.running ? 'running'
    : (result?.success && activePdbUrl) ? 'done'
    : result && !result.running && !result.success ? 'error'
    : phase === 'running' ? 'running'
    : 'idle'

  // ── Download handlers ─────────────────────────────────────────────────────
  function handleDownloadInp() {
    if (!result?.inpContent) return
    downloadInp(result.inpContent, 'structure')
  }

  function handleDownloadPdb() {
    if (!activePdbUrl) return
    const engine = displayVariant === 'alternative' ? 'alt' : 'std'
    const step   = steps[engine] ?? null
    const suffix = step && frames.length > 1 ? `${engine}_${step}` : engine
    const a = document.createElement('a')
    a.href     = activePdbUrl
    a.download = `structure_${suffix}.pdb`
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

      {/* ── Results panel ────────────────────────────────────────────────── */}
      {(phase !== 'idle') && (
        <div className={styles.resultsPanel}>
          {/* Action bar */}
          {result?.success && (
            <div className={styles.actionBar}>
              <div className={styles.actionLeft} />
              <div className={styles.actionRight}>
                {result.inpContent && (
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

          {/* Energy bar + iteration slider */}
          {result?.success && (result.stdEnergy != null || result.altEnergy != null) && (
            <div style={{ borderBottom: '1px solid var(--border)', background: 'var(--surface2)' }}>
              <div className={styles.energyBar}>
                <span className={styles.energyBarLabel}>Energy</span>
                <EnergyBadge
                  label="Standard"
                  energy={stdStepEnergy}
                  isWinner={result.winner === 'standard'}
                  isActive={displayVariant === 'standard'}
                  onClick={() => handleVariantToggle('standard')}
                />
                {result.altEnergy != null && (
                  <EnergyBadge
                    label="Alternative"
                    energy={altStepEnergy}
                    isWinner={result.winner === 'alternative'}
                    isActive={displayVariant === 'alternative'}
                    onClick={() => handleVariantToggle('alternative')}
                  />
                )}
                <span className={styles.energyBarHint}>lower = better minimization</span>
              </div>
              {frames.length > 1 && (
                <IterationSlider
                  frames={frames}
                  activeStep={activeStep}
                  bestStep={bestStep}
                  onStep={step => setSteps(prev => ({
                    ...prev,
                    [displayVariant === 'alternative' ? 'alt' : 'std']: step,
                  }))}
                />
              )}
            </div>
          )}

          {/* Error */}
          {result?.success === false && !result.running && (
            <div className={styles.metaRow}>
              <div className={styles.metaError}>
                3D model failed: {result.error}
              </div>
            </div>
          )}

          {/* Generated .inp */}
          {result?.inpContent && (
            <details className={styles.inpDetails}>
              <summary className={styles.inpSummary}>Generated .inp input</summary>
              <pre className={styles.inpPre}>
                {result.qrs ? `# qrs    ${result.qrs}\n\n` : ''}
                {filterInpDisplay(result.inpContent)}
              </pre>
            </details>
          )}

          {/* Quadro stdout/stderr — only on failure */}
          {result?.quadroOutput && (
            <details className={styles.inpDetails} open>
              <summary className={styles.inpSummary}>Quadro output (stdout / stderr)</summary>
              <pre className={styles.inpPre}>{result.quadroOutput}</pre>
            </details>
          )}

          {/* 3D viewer */}
          <div className={styles.viewerWrap}>
            <MolstarViewer
              pdbUrl={activePdbUrl}
              runState={viewerState}
              runStatus={result?.error ?? ''}
              structureName="G4"
            />
          </div>

          {/* Analysis — eltetrado vs input comparison */}
          {result?.success && (result.eltetradoOutput || result.eltetradoError) && (
            <details className={styles.inpDetails}>
              <summary className={styles.inpSummary}>Analysis</summary>

              {/* Info strip: QRS + ViennaRNA */}
              {(result.qrs || result.rnaStructure) && (
                <div style={{
                  borderTop: '1px solid var(--border)',
                  padding: '10px 16px',
                  background: 'var(--surface2)',
                  fontFamily: 'var(--mono)',
                  fontSize: 12,
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 4,
                }}>
                  {result.qrs && (
                    <div>
                      <span style={{ color: 'var(--text-dim)', display: 'inline-block', width: 80 }}>qrs</span>
                      <span>{result.qrs}</span>
                    </div>
                  )}
                  {result.rnaStructure && (
                    <div>
                      <span style={{ color: 'var(--text-dim)', display: 'inline-block', width: 80 }}>rnafold</span>
                      <span>{result.rnaStructure}</span>
                    </div>
                  )}
                </div>
              )}

              {/* Side-by-side: Input vs eltetrado */}
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 0, borderTop: '1px solid var(--border)' }}>
                <div style={{ borderRight: '1px solid var(--border)', padding: '12px 16px' }}>
                  <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: 1, marginBottom: 8 }}>
                    Input
                  </div>
                  <pre className={styles.inpPre} style={{ margin: 0 }}>
                    {filterInpDisplay(result.inpContent ?? '')}
                  </pre>
                </div>
                <div style={{ padding: '12px 16px' }}>
                  <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: 1, marginBottom: 8 }}>
                    eltetrado
                  </div>
                  {result.eltetradoOutput
                    ? <pre className={styles.inpPre} style={{ margin: 0 }}>{result.eltetradoOutput}</pre>
                    : <div style={{ fontSize: 12, color: 'var(--err-text)' }}>{result.eltetradoError ?? 'Analysis unavailable'}</div>
                  }
                </div>
              </div>
            </details>
          )}
        </div>
      )}

      {/* ── Idle placeholder ─────────────────────────────────────────────── */}
      {phase === 'idle' && (
        <div className={styles.idlePlaceholder}>
          <G4Icon />
          <p className={styles.idleText}>
            Enter a nucleotide sequence and click <strong>Run Pipeline</strong> to
            predict the 3D G-quadruplex structure.
          </p>
          <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 8 }}>
            Uppercase = RNA · lowercase = DNA
          </p>
        </div>
      )}
    </div>
  )
}

// ── Sub-components ────────────────────────────────────────────────────────────

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

const HIDDEN_INP_KEYS = /^(test|rm_level|iteration_steps|iteration)\s/i
function filterInpDisplay(text) {
  if (!text) return text
  return text.split('\n')
    .filter(l => !HIDDEN_INP_KEYS.test(l.trim()))
    .map(line => {
      const trimmed = line.trim()
      if (!trimmed) return ''
      const spaceIdx = trimmed.search(/\s/)
      if (spaceIdx < 0) return trimmed
      const key   = trimmed.slice(0, spaceIdx)
      const value = trimmed.slice(spaceIdx).trimStart()
      return key.padEnd(12) + value
    })
    .join('\n')
}
