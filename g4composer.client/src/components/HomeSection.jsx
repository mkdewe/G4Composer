import { useState, useRef, useCallback, useEffect } from 'react'
import MolstarViewer from './MolstarViewer.jsx'
import {
  runPipeline, fetchPipelinePdb, fetchPipelineAltPdb, fetchFrame,
  fetchSilvaGroups, fetchExampleDetail, runQuadro11Stream,
} from '../services/apiService.js'
import { downloadInp, parseInp, serializeInp } from '../utils/inpSerializer.js'
import styles from './HomeSection.module.css'

export default function HomeSection({ onEditInBuild }) {
  const [sequence,  setSequence]  = useState('')
  const [phase,     setPhase]     = useState('idle')   // 'idle' | 'running' | 'done'
  const [result,    setResult]    = useState(null)     // null | { running, success, ... }
  const [steps,     setSteps]     = useState({ std: null, alt: null })
  const [progress,  setProgress]  = useState(null)   // { stage, label, percent, detail } — live stage
  const [displayedPdbUrl, setDisplayedPdbUrl] = useState(null)
  const [activeSource, setActiveSource] = useState(null)   // 'aligner' | 'prediction' | null (=default)
  const frameCacheRef = useRef({})
  const abortRef      = useRef(null)

  // ── Example sequences (deposited ONQuadro G4 structures) ───────────────────
  // Each example carries its FULL deposited .inp input. Picking one loads the
  // sequence into the field and holds the deposited input in the background; on
  // Run we compute the model directly via Quadro from that input — skipping
  // RNAfold / QRS / topology inference entirely (Build-G4-identical path).
  const [examples,      setExamples]      = useState([])    // [{ pdbId, note, tetrads }]
  const [examplesOpen,  setExamplesOpen]  = useState(false)
  const [loadingExample, setLoadingExample] = useState(false)
  // Full deposited input of the currently loaded example. Cleared the moment the
  // user edits the sequence by hand (it's no longer that example → fall back to
  // the pipeline).
  const [loadedExampleInput, setLoadedExampleInput] = useState(null)  // QuadroInput | null
  const examplesBoxRef = useRef(null)

  // Load the example catalogue once — flat list of deposited structures.
  useEffect(() => {
    let cancelled = false
    fetchSilvaGroups().then(groups => {
      if (cancelled || !groups) return
      const flat = []
      for (const g of groups)
        for (const sub of g.subtypes ?? [])
          for (const ex of sub.examples ?? [])
            if (!ex.isTheoretical)
              flat.push({ pdbId: ex.pdbId, tetrads: ex.tetrads, note: ex.note })
      // De-dupe by pdbId and sort alphabetically for a stable flat list.
      const seen = new Set()
      const list = flat.filter(e => !seen.has(e.pdbId) && seen.add(e.pdbId))
      list.sort((a, b) => a.pdbId.localeCompare(b.pdbId))
      setExamples(list)
    })
    return () => { cancelled = true }
  }, [])

  // Close the dropdown on outside click.
  useEffect(() => {
    if (!examplesOpen) return
    const onDown = e => {
      if (examplesBoxRef.current && !examplesBoxRef.current.contains(e.target)) setExamplesOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [examplesOpen])

  const loadExampleSequence = useCallback(async (ex) => {
    setExamplesOpen(false)
    setLoadingExample(true)
    try {
      const detail = await fetchExampleDetail(ex.pdbId)
      if (detail?.sequence) {
        const seq = detail.sequence
        // Build the exact QuadroInput from the deposited .inp (same shape Build
        // submits). Path is a ';'-joined string in the DB → split to a list.
        // Sugar pucker isn't stored — derive from sequence case (UPPER→N, lower→S).
        const input = {
          name:      detail.inpName || ex.pdbId,
          sequence:  seq,
          structure: detail.structure ?? '',
          chi:       detail.chi ?? '',
          orient:    detail.orient || 'A+;B-',
          rise:      String(detail.rise ?? '3.4'),
          twist:     String(detail.twist ?? '29'),
          path:      detail.path
            ? String(detail.path).split(';').map(s => s.trim()).filter(Boolean)
            : null,
          Shugar:    seq.split('').map(c => /[A-Z]/.test(c) ? 'N' : 'S').join(''),
        }
        setSequence(seq)
        setLoadedExampleInput(input)
      }
    } finally {
      setLoadingExample(false)
    }
  }, [])

  // ── Run pipeline ──────────────────────────────────────────────────────────
  const handleRun = useCallback(async () => {
    const seq = sequence.trim()
    if (!seq) return

    setPhase('running')
    setResult(null)
    setSteps({ std: null, alt: null })
    setProgress(null)
    setDisplayedPdbUrl(null)
    setActiveSource(null)   // back to the default tab (aligner if present, else prediction)

    // ── Example path ────────────────────────────────────────────────────────
    // A deposited example is loaded and the field still matches it → compute the
    // model straight from the deposited .inp via Quadro (same engine as Build/
    // Batch). No RNAfold, no QRS, no topology inference.
    if (loadedExampleInput && loadedExampleInput.sequence === sequence) {
      const abort = new AbortController()
      abortRef.current = abort
      setResult({ running: true })
      try {
        const r = await runQuadro11Stream([loadedExampleInput], { onProgress: setProgress, signal: abort.signal })
        const pdbUrl = URL.createObjectURL(r.blob)
        setSteps({ std: r.stdBestStep ?? null, alt: r.altBestStep ?? null })
        setResult({
          running: false, success: true, jobId: r.jobId,
          stdEnergy: r.stdEnergy, altEnergy: r.altEnergy,
          winner: r.winner, hasAlt: r.hasAlt,
          pdbUrl, altPdbUrl: r.altUrl ?? null,
          stdFrames: r.stdFrames ?? [], altFrames: r.altFrames ?? [],
          stdBestStep: r.stdBestStep ?? null, altBestStep: r.altBestStep ?? null,
          displayVariant: r.winner ?? 'standard',
          inpContent: serializeInp(loadedExampleInput),
          exampleInput: loadedExampleInput,
          error: null,
        })
      } catch (err) {
        // Stop button aborts the request — that's a user action, not an error.
        if (abort.signal.aborted || err?.name === 'AbortError')
          setResult(null)
        else
          setResult({ running: false, success: false, error: err.message })
      } finally {
        setProgress(null)
        setPhase('done')
      }
      return
    }

    const abort = new AbortController()
    abortRef.current = abort

    try {
      for await (const event of runPipeline(seq, false, abort.signal)) {
        if (abort.signal.aborted) break

        switch (event.type) {
          case 'progress':
            setProgress(event)
            break

          case 'aligner_quadro_start':
            setResult({ running: true })
            break

          case 'aligner_quadro_done': {
            const { success, jobId, qrs, rnaStructure, stdEnergy, altEnergy, winner,
                    hasAlt, error, inpContent, quadroOutput, combinedStructure,
                    stdFrames: stdFramesMeta, altFrames: altFramesMeta,
                    stdBestStep, altBestStep,
                    eltetradoOutput, eltetradoError,
                    candidates, determination, topologyLabel, topologyRationale, loopNotation } = event
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
                  candidates: candidates ?? null,
                  determination: determination ?? null,
                  selectedJobId: jobId,
                  topologyLabel: topologyLabel ?? null,
                  topologyRationale: topologyRationale ?? null,
                  loopNotation: loopNotation ?? null,
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
            setProgress(null)
            setPhase('done')
            break

          case 'error':
            setProgress(null)
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
  }, [sequence, loadedExampleInput])

  const handleStop = useCallback(() => {
    abortRef.current?.abort()   // closes the SSE/stream connection → backend cancels via RequestAborted
    setProgress(null)
    setResult(null)
    setPhase('idle')
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

  function handleEditInBuild() {
    if (!onEditInBuild) return
    // Example run: carry the EXACT deposited input. Build recovers the Silva
    // group/subtype by inverting the path (classifyPath), no hint needed.
    if (result?.exampleInput) {
      onEditInBuild({ ...result.exampleInput })
      return
    }
    if (!result?.inpContent) return
    onEditInBuild(parseInp(result.inpContent))
  }

  // Switch the displayed model to another predicted topology candidate. Each candidate
  // has its own jobId / PDB / frames stored server-side, so we just refetch by jobId.
  const selectCandidate = useCallback(async (c) => {
    if (!c || !c.success || !c.jobId) return
    const pdbUrl    = await fetchPipelinePdb(c.jobId)
    const altPdbUrl = c.hasAlt ? await fetchPipelineAltPdb(c.jobId) : null
    frameCacheRef.current = {}
    setSteps({ std: c.stdBestStep ?? null, alt: c.altBestStep ?? null })
    setDisplayedPdbUrl(null)
    setResult(r => ({
      ...r,
      selectedJobId: c.jobId,
      jobId: c.jobId,
      stdEnergy: c.stdEnergy, altEnergy: c.altEnergy,
      winner: c.winner, hasAlt: c.hasAlt,
      pdbUrl, altPdbUrl,
      stdFrames: c.stdFrames ?? [], altFrames: c.altFrames ?? [],
      stdBestStep: c.stdBestStep ?? null, altBestStep: c.altBestStep ?? null,
      combinedStructure: c.combinedStructure ?? null,
      displayVariant: c.winner ?? 'standard',
      inpContent: c.inpContent,
      topologyLabel: c.label, topologyRationale: c.rationale, loopNotation: c.loopNotation,
    }))
  }, [])

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
            onChange={e => { setSequence(e.target.value); setLoadedExampleInput(null) }}
            placeholder="e.g. GGGGAAGGGGAAGGGGAAGGGG  (uppercase = RNA, lowercase = DNA)"
            spellCheck={false}
            disabled={phase === 'running'}
            onKeyDown={e => e.key === 'Enter' && phase !== 'running' && handleRun()}
          />
        </div>

        {/* Examples dropdown — known G4 sequences from the structure database */}
        {examples.length > 0 && (
          <div ref={examplesBoxRef} style={{ position: 'relative' }}>
            <button
              type="button"
              className={styles.exampleBtn}
              onClick={() => setExamplesOpen(o => !o)}
              disabled={phase === 'running' || loadingExample}
              title="Load a known G4 sequence from the database"
            >
              {loadingExample ? 'Loading…' : 'Examples'}
              <svg width="10" height="10" viewBox="0 0 12 12" fill="none" style={{ marginLeft: 2 }}>
                <path d="M2 4l4 4 4-4" stroke="currentColor" strokeWidth="1.5"
                      strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            </button>
            {examplesOpen && (
              <div className={styles.exampleMenu}>
                {examples.map(ex => (
                  <button
                    key={ex.pdbId}
                    type="button"
                    className={styles.exampleItem}
                    onClick={() => loadExampleSequence(ex)}
                    title={ex.note}
                  >
                    <span className={styles.exampleItemId}>{ex.pdbId.toUpperCase().replace(/^_/, '')}</span>
                    <span className={styles.exampleItemNote}>{ex.note}</span>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
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
                {result.inpContent && onEditInBuild && (
                  <button
                    className={styles.actionBtn}
                    onClick={handleEditInBuild}
                    title="Open these parameters in the Build G4 form to edit and re-run"
                  >
                    ✎ Edit in Build G4
                  </button>
                )}
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

          {/* Topology candidates — two groups: ONQuadro aligner (default) vs sequence prediction */}
          {result?.success && result.candidates && result.candidates.length > 1 && (() => {
            const cands        = result.candidates
            const alignerCands = cands.filter(c => c.source === 'aligner')
            const predCands    = cands.filter(c => c.source !== 'aligner')
            const hasAligner   = alignerCands.length > 0
            const hasPred      = predCands.length > 0
            const effSource    = activeSource ?? (hasAligner ? 'aligner' : 'prediction')
            const groupCands   = effSource === 'prediction' ? predCands : alignerCands

            const bestOf = list => list
              .filter(c => c.success)
              .reduce((a, c) => (a == null || Number(c.stdEnergy) < Number(a.stdEnergy) ? c : a), null)
            const bestJob    = bestOf(groupCands)?.jobId
            const activeCand = groupCands.find(c => c.jobId === result.selectedJobId)

            const switchSource = src => {
              if (src === effSource) return
              setActiveSource(src)
              const best = bestOf(src === 'prediction' ? predCands : alignerCands)
              if (best) selectCandidate(best)
            }

            return (
              <>
                {/* Source selector — only when both groups exist */}
                {hasAligner && hasPred && (
                  <div className={styles.topoTabBar}>
                    <span className={styles.topoTabBarLabel}>Source</span>
                    <button type="button" onClick={() => switchSource('aligner')}
                      className={[styles.topoTab, effSource === 'aligner' && styles.topoTabActive].filter(Boolean).join(' ')}>
                      <span className={styles.topoTabName}>ONQuadro aligner
                        <span className={styles.topoTabNotation}>{alignerCands.length} template{alignerCands.length > 1 ? 's' : ''}</span>
                      </span>
                    </button>
                    <button type="button" onClick={() => switchSource('prediction')}
                      className={[styles.topoTab, effSource === 'prediction' && styles.topoTabActive].filter(Boolean).join(' ')}>
                      <span className={styles.topoTabName}>Sequence prediction
                        <span className={styles.topoTabNotation}>{predCands.length} fold{predCands.length > 1 ? 's' : ''}</span>
                      </span>
                    </button>
                  </div>
                )}

                <div className={styles.topoTabBar}>
                  <span className={styles.topoTabBarLabel}>
                    {effSource === 'prediction' ? 'Prediction' : 'Template'} ({groupCands.length})
                  </span>
                  <TopoScroller>
                    {groupCands.map((c, i) => {
                      const sel  = c.jobId === result.selectedJobId
                      const name = (c.label || '').split(' (')[0]
                      const e = c.success
                        ? (c.stdEnergy != null ? Number(c.stdEnergy).toFixed(1) : '—')
                        : 'no model'
                      return (
                        <button
                          key={c.jobId || i}
                          type="button"
                          disabled={!c.success}
                          onClick={() => selectCandidate(c)}
                          title={c.rationale || ''}
                          className={[
                            styles.topoTab,
                            sel && styles.topoTabActive,
                            !c.success && styles.topoTabFailed,
                          ].filter(Boolean).join(' ')}
                        >
                          <span className={styles.topoTabName}>
                            {name}
                            <span className={styles.topoTabNotation}>{c.loopNotation}</span>
                            {c.success && c.jobId === bestJob && (
                              <span className={styles.topoTabBest}>best</span>
                            )}
                          </span>
                          <span className={styles.topoTabMeta}>
                            <span>E = {e}</span>
                            <span className={styles.topoTabConf}>· {c.confidence}</span>
                          </span>
                        </button>
                      )
                    })}
                  </TopoScroller>
                </div>
                {activeCand?.rationale && (
                  <div className={styles.topoNote} style={{ background: 'var(--surface2)' }}>
                    Showing <strong>{(activeCand.label || '').split(' (')[0]}</strong> ({activeCand.loopNotation}) — {activeCand.rationale}.
                    The lowest-energy model is marked <em>best</em>; switch tabs to compare.
                  </div>
                )}
              </>
            )
          })()}

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
              representation="cartoon"
              progress={viewerState === 'running' ? progress : null}
            />
          </div>

          {/* Analysis — topology selection procedure + eltetrado vs input comparison */}
          {result?.success && (result.determination || result.candidates?.length || result.eltetradoOutput || result.eltetradoError) && (
            <details className={styles.inpDetails} open>
              <summary className={styles.inpSummary}>Analysis</summary>

              {/* Topology selection — how the candidate set was determined + why this model won */}
              {(result.determination || result.candidates?.length > 0) && (
                <TopologySelection
                  determination={result.determination}
                  candidates={result.candidates ?? []}
                  selectedJobId={result.selectedJobId}
                />
              )}

              {/* Experimental templates the ONQuadro aligner matched — real deposited G4 structures
                  that ground the model. Shown as biological reference examples, without prior
                  probabilities (those belong to the sequence predictor, not to observed structures). */}
              {(result.candidates ?? []).some(c => c.source === 'aligner') && (
                <AlignerExamples candidates={result.candidates.filter(c => c.source === 'aligner')} />
              )}

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

// Horizontal sliding strip for the topology chips — there can be many (every fold with P>0 is
// modelled), so the row scrolls and arrow buttons appear/enable only when it overflows.
function TopoScroller({ children }) {
  const ref = useRef(null)
  const [atStart, setAtStart] = useState(true)
  const [atEnd, setAtEnd] = useState(true)

  const update = useCallback(() => {
    const el = ref.current
    if (!el) return
    setAtStart(el.scrollLeft <= 2)
    setAtEnd(el.scrollLeft + el.clientWidth >= el.scrollWidth - 2)
  }, [])

  useEffect(() => {
    update()
    const el = ref.current
    if (!el) return
    el.addEventListener('scroll', update, { passive: true })
    window.addEventListener('resize', update)
    return () => { el.removeEventListener('scroll', update); window.removeEventListener('resize', update) }
  }, [update, children])

  const scroll = (dir) => ref.current?.scrollBy({ left: dir * 240, behavior: 'smooth' })

  return (
    <div className={styles.topoScroller}>
      <button type="button" className={styles.topoArrow} onClick={() => scroll(-1)}
              disabled={atStart} aria-label="Scroll topologies left">‹</button>
      <div className={styles.topoTabs} ref={ref}>{children}</div>
      <button type="button" className={styles.topoArrow} onClick={() => scroll(1)}
              disabled={atEnd} aria-label="Scroll topologies right">›</button>
    </div>
  )
}

// The experimental PDB templates the ONQuadro aligner matched against the query. These are real,
// deposited G4 structures — shown as biological reference points for the predicted fold. Deliberately
// no prior probabilities / percentages here: those are properties of the sequence predictor, whereas
// these are observed structures the template-geometry models were literally built from.
function AlignerExamples({ candidates = [] }) {
  if (!candidates.length) return null
  // Template names look like "6w9p-assembly1"; the leading 4 chars are the RCSB PDB id.
  const pdbId = label => (label || '').split(' (')[0].slice(0, 4).toUpperCase()
  // Loop lengths, alignment metrics and viability live in the rationale string; pull them out.
  // tract_distance = G-tract superposition fit (Å, lower = better); linker_score = loop/linker
  // compatibility (higher = better) — these ARE the aligner's match quality.
  const detail = r => ({
    loops:  /loops?\s+([\d-]+)/i.exec(r.rationale || '')?.[1] ?? null,
    viab:   /viability=([a-z_/]+)/i.exec(r.rationale || '')?.[1] ?? null,
    tract:  /tract_distance=(-?[\d.]+)/i.exec(r.rationale || '')?.[1] ?? null,
    linker: /linker_score=(-?[\d.]+)/i.exec(r.rationale || '')?.[1] ?? null,
  })
  const cell = { padding: '6px 9px', borderBottom: '1px solid var(--border)', verticalAlign: 'top', whiteSpace: 'nowrap' }
  const head = { ...cell, fontSize: 10, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: 0.5 }

  return (
    <div style={{ borderTop: '1px solid var(--border)', padding: '12px 16px' }}>
      <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: 1, marginBottom: 6 }}>
        ONQuadro aligner — experimental templates
      </div>
      <p style={{ fontSize: 12, color: 'var(--text-dim)', margin: '0 0 10px', lineHeight: 1.5 }}>
        Deposited PDB G-quadruplexes the aligner matched to this sequence — the real folds the
        template-geometry models were built from. One template per topology is kept (the best match);
        <strong> tract dist.</strong> (Å, lower = better G-tract fit) and <strong>linker</strong> score
        (higher = better) are the aligner's match quality.
      </p>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12, fontFamily: 'var(--mono)' }}>
          <thead>
            <tr>
              <th style={{ ...head, textAlign: 'left' }}>Template (PDB)</th>
              <th style={{ ...head, textAlign: 'left' }}>Topology</th>
              <th style={{ ...head, textAlign: 'left' }}>Loops</th>
              <th style={{ ...head, textAlign: 'right' }} title="G-tract superposition distance (Å) — lower is a better fit">Tract dist.</th>
              <th style={{ ...head, textAlign: 'right' }} title="Loop/linker compatibility — higher is better">Linker</th>
              <th style={{ ...head, textAlign: 'left' }}>Viability</th>
            </tr>
          </thead>
          <tbody>
            {candidates.map((c, i) => {
              const id = pdbId(c.label)
              const { loops, viab, tract, linker } = detail(c)
              return (
                <tr key={c.jobId || i}>
                  <td style={cell}>
                    <a href={`https://www.rcsb.org/structure/${id}`} target="_blank" rel="noreferrer"
                       style={{ color: 'var(--teal)', textDecoration: 'none' }}
                       title={`Open ${id} on RCSB PDB`}>
                      {(c.label || '').split(' (')[0]}
                    </a>
                  </td>
                  <td style={cell}>{c.loopNotation || '—'}</td>
                  <td style={cell}>{loops || '—'}</td>
                  <td style={{ ...cell, textAlign: 'right' }}>{tract ?? '—'}</td>
                  <td style={{ ...cell, textAlign: 'right' }}>{linker ?? '—'}</td>
                  <td style={{ ...cell, fontFamily: 'var(--sans)' }}>{viab || 'n/a'}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

// Shows HOW the candidate set was determined and WHY one model won. Combines two pieces of
// data: the determination (full Webba da Silva scored space → which folds were even considered
// and with what prior probability) and the built candidates (their actual minimised energy).
function TopologySelection({ determination, candidates = [], selectedJobId }) {
  // Map built candidates (with energy) by loop notation so each scored fold can show its energy.
  const builtByNotation = new Map()
  for (const c of candidates) if (c.loopNotation) builtByNotation.set(c.loopNotation, c)

  const okEnergies = candidates.filter(c => c.success && c.stdEnergy != null).map(c => Number(c.stdEnergy))
  const bestEnergy = okEnergies.length ? Math.min(...okEnergies) : null

  // Rows = the full determination space when available, else fall back to the built candidates.
  const rows = determination?.ranked?.length
    ? determination.ranked.map(s => ({
        notation: s.loopNotation, label: s.label, confidence: s.confidence,
        rationale: s.rationale, score: s.score, probability: s.probability, built: s.built,
        excludedReason: s.excludedReason ?? null,
        cand: builtByNotation.get(s.loopNotation) ?? null,
      }))
    : candidates.map(c => ({
        notation: c.loopNotation, label: c.label, confidence: c.confidence,
        rationale: c.rationale, score: null, probability: null, built: true,
        excludedReason: null, cand: c,
      }))

  const cell = { padding: '6px 9px', borderBottom: '1px solid var(--border)', verticalAlign: 'top', whiteSpace: 'nowrap' }
  const head = { ...cell, fontSize: 10, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: 0.5 }
  const loops = determination?.loops
  const aRich = determination?.aRichLoops ?? []
  const builtCount = rows.filter(r => r.built).length

  return (
    <div style={{ borderTop: '1px solid var(--border)', padding: '12px 16px' }}>
      <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-dim)', textTransform: 'uppercase', letterSpacing: 1, marginBottom: 6 }}>
        Topology selection
      </div>

      {/* The determinant: the inputs that fix the allowed fold space. */}
      {determination && (
        <div style={{
          display: 'flex', flexWrap: 'wrap', gap: '6px 16px', marginBottom: 10,
          fontFamily: 'var(--mono)', fontSize: 12,
        }}>
          <span><span style={{ color: 'var(--text-dim)' }}>tetrads (N) </span><strong>{determination.tetrads}</strong></span>
          {loops && (
            <span>
              <span style={{ color: 'var(--text-dim)' }}>loop lengths </span>
              <strong>{loops.join(' · ')}</strong>
              {aRich.some(Boolean) && (
                <span style={{ color: 'var(--text-dim)' }}> (A-rich: {aRich.map((a, i) => a ? `l${i + 1}` : null).filter(Boolean).join(', ')})</span>
              )}
            </span>
          )}
          <span><span style={{ color: 'var(--text-dim)' }}>molecule </span><strong>{determination.isRna ? 'RNA' : 'DNA'}</strong></span>
        </div>
      )}

      <p style={{ fontSize: 12, color: 'var(--text-dim)', margin: '0 0 10px', lineHeight: 1.5 }}>
        The candidate set is restricted to the <strong>canonical Webba da Silva topologies</strong>
        (experimentally observed folds) and filtered by loop-length sterics — only folds that can
        thread all four G-tracts survive. Each is scored as <strong>likelihood × nature</strong>: the
        loop-length likelihood (per-loop priors, RNA gate, A-track effect) times the
        <strong>natural-occurrence prior</strong> P(topology) measured from deposited G4 structures
        (parallel P-P-P ≈ 51%). The <strong>probability</strong> below is that score normalised over the
        admissible space (a heuristic prior, not a calibrated probability). The top folds — <strong>plus
        any tied at the cutoff</strong>, so equally-ranked topologies are never dropped — are built with
        Quadro ({builtCount} here); the model with the <strong>lowest energy</strong> (kcal/mol) is
        selected. Folds that were ruled out (sterically impossible, forbidden combination, can't thread,
        or penalised to ≤0) are listed at <strong>0%</strong> with the reason.
      </p>

      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12, fontFamily: 'var(--mono)' }}>
          <thead>
            <tr>
              <th style={{ ...head, textAlign: 'left' }}>Topology</th>
              <th style={{ ...head, textAlign: 'left' }}>Loops</th>
              <th style={{ ...head, textAlign: 'right' }} title="Normalised loop-prior product">Prob.</th>
              <th style={{ ...head, textAlign: 'right' }}>E std</th>
              <th style={{ ...head, textAlign: 'right' }}>ΔE vs best</th>
              <th style={{ ...head, textAlign: 'left' }}>Verdict</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => {
              const c        = r.cand
              const excluded = !!r.excludedReason
              const e        = c?.success && c.stdEnergy != null ? Number(c.stdEnergy) : null
              const isBest   = e != null && bestEnergy != null && e === bestEnergy
              const delta    = e != null && bestEnergy != null ? e - bestEnergy : null
              const isShown  = c?.jobId && c.jobId === selectedJobId
              const name     = (r.label || '').split(' (')[0]
              return (
                <tr key={r.notation || i} style={{
                  background: isBest ? 'var(--teal-light)' : 'transparent',
                  opacity: excluded ? 0.6 : 1,
                }}>
                  <td style={{ ...cell, fontWeight: isBest ? 700 : 400 }}>
                    {name}
                    {isShown && <span style={{ marginLeft: 6, fontSize: 10, color: 'var(--teal-dark)' }}>(shown)</span>}
                  </td>
                  <td style={cell}>{r.notation || '—'}</td>
                  <td style={{ ...cell, textAlign: 'right' }}>{r.probability != null ? `${r.probability}%` : '—'}</td>
                  <td style={{ ...cell, textAlign: 'right' }}>{e != null ? e.toFixed(1) : '—'}</td>
                  <td style={{ ...cell, textAlign: 'right' }}>
                    {delta == null ? '—' : isBest ? '0.0' : `+${delta.toFixed(1)}`}
                  </td>
                  <td style={{ ...cell, fontFamily: 'var(--sans)', whiteSpace: 'normal' }}>
                    {excluded
                      ? <span style={{ color: 'var(--text-dim)' }}>ruled out — {r.excludedReason}</span>
                      : isBest
                        ? <span style={{ color: 'var(--teal-dark)', fontWeight: 700 }}>★ selected (lowest energy)</span>
                        : c?.success
                          ? <span style={{ color: 'var(--text-dim)' }}>built · higher energy</span>
                          : c && !c.success
                            ? <span style={{ color: 'var(--err-text)' }} title={c.error || ''}>built · no model</span>
                            : r.built
                              ? <span style={{ color: 'var(--text-dim)' }}>modelled</span>
                              : <span style={{ color: 'var(--text-dim)', opacity: 0.7 }}>not modelled (below cutoff)</span>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      {/* How the winner was scored — the explicit probability arithmetic for the selected fold. */}
      {(() => {
        const winner = rows.find(r =>
          r.cand?.success && bestEnergy != null && Number(r.cand.stdEnergy) === bestEnergy)
        if (!winner) return null
        return (
          <div style={{
            marginTop: 12, padding: '10px 12px', background: 'var(--teal-light)',
            border: '1px solid var(--teal)', borderRadius: 6, fontSize: 12, lineHeight: 1.6,
          }}>
            <div style={{ fontWeight: 700, marginBottom: 4 }}>
              How the winner was scored — {(winner.label || '').split(' (')[0]} ({winner.notation})
            </div>
            <div style={{ fontFamily: 'var(--mono)', fontSize: 11 }}>{winner.rationale}</div>
            <div style={{ marginTop: 4 }}>
              → normalised over {builtCount} admissible folds = <strong>{winner.probability}%</strong> prior probability.
              It was then <strong>selected by lowest minimised energy</strong> ({Number(winner.cand.stdEnergy).toFixed(1)} kcal/mol):
              the loop-prior probability ranks candidates a priori, the computed energy decides the winner.
            </div>
          </div>
        )
      })()}

      {/* Per-topology rationale: the same loop-prior arithmetic for every admissible fold. */}
      {rows.some(r => r.rationale) && (
        <details style={{ marginTop: 10 }}>
          <summary style={{ fontSize: 11, color: 'var(--text-dim)', cursor: 'pointer' }}>
            Loop-prior breakdown for every fold
          </summary>
          <div style={{ marginTop: 6, display: 'flex', flexDirection: 'column', gap: 5 }}>
            {rows.filter(r => r.rationale).map((r, i) => (
              <div key={r.notation || i} style={{ fontSize: 11, color: 'var(--text-dim)', lineHeight: 1.5 }}>
                <strong style={{ color: 'var(--text)' }}>{(r.label || '').split(' (')[0]}</strong>
                {r.notation ? ` (${r.notation})` : ''} — {r.rationale}
              </div>
            ))}
          </div>
        </details>
      )}
    </div>
  )
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
