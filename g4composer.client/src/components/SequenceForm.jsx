import { useState, useEffect, useRef, useCallback } from 'react'
import {
  parseInputLines,
  validateEntry,
  defaultPolarityLabel,
  countTetrads,
  orientFromGroup,
} from '../utils/sequenceParser.js'
import { fetchSilvaGroups, fetchExampleDetail, fetchNonCanonicalExamples } from '../services/apiService.js'
import { buildPath, buildDefaultTwist, scaleOrientToTetrads } from '../utils/silvaTopology.js'
import styles from './SequenceForm.module.css'

// ── Silva classification data ─────────────────────────────────────────────────

// ── Component ─────────────────────────────────────────────────────────────────

export default function SequenceForm({ onRun, runState }) {
  // ── Database-driven classification data ──────────────────────────────────
  const [silvaData,     setSilvaData]     = useState(null)   // null = loading
  const [silvaError,    setSilvaError]    = useState(false)

  useEffect(() => {
    fetchSilvaGroups().then(data => {
      if (data) setSilvaData(data)
      else setSilvaError(true)
    })
    fetchNonCanonicalExamples().then(data => {
      if (data?.length) setNonCanonical(data)
    })
  }, [])

  // ── Canonical vs Non-canonical mode ─────────────────────────────────────
  const [mode, setMode] = useState('canonical')  // 'canonical' | 'noncanonical'
  const [nonCanonical, setNonCanonical] = useState([])  // non-canonical examples from DB

  const [nameVal,       setNameVal]       = useState('')
  const [seqVal,        setSeqVal]        = useState('')
  const [structVal,     setStructVal]     = useState('')
  const [pathVal,       setPathVal]       = useState('')
  const [chiVal,        setChiVal]        = useState('')
  const [sugarVal,      setShugarVal]     = useState('')  // named setShugarVal for quadro compat
  const [orientVal,     setOrientVal]     = useState('A+;B-')
  const [silvaGroup,    setSilvaGroup]    = useState('UDUD')
  const [subtype,       setSubtype]       = useState('6a')
  const [advOpen,       setAdvOpen]       = useState(false)
  const [twist,         setTwist]         = useState('29')
  const [rise,          setRise]          = useState('3.4')
  const [pucker,        setPucker]        = useState('S')
  const [iterations,    setIterations]    = useState(100)
  const [rmLevel,       setRmLevel]       = useState(0)
  const [isTest,        setIsTest]        = useState(false)
  const [parseError,    setParseError]    = useState(null)
  const [touched,       setTouched]       = useState(new Set())  // fields user has visited

  const isRunning = runState === 'running'
  const errorRef = useRef(null)  // ref for scroll-to-error

  // ── Live validation — computed on every render ───────────────────────────
  const validationErrors = useCallback(() => {
    const errs = []
    const rawSeq = seqVal.trim()
    const seq    = rawSeq.toLowerCase()
    const struct = structVal.trim()

    if (!seq) {
      errs.push('Sequence is required')
    } else if (seq.length < 4) {
      errs.push('Sequence is too short (minimum 4 nucleotides)')
    } else {
      // UPPERCASE = RNA (A,C,G,U) · lowercase = DNA (a,c,g,t)
      // Mixed RNA/DNA sequences are allowed.
      // Invalid: uppercase T, lowercase u
      if (/T/.test(rawSeq))
        errs.push("Sequence contains uppercase 'T' — RNA residues use 'U' (uppercase)")
      if (/u/.test(rawSeq))
        errs.push("Sequence contains lowercase 'u' — invalid: DNA uses 't', RNA uses 'U' (uppercase)")
      if (!/^[ACGUacgt]+$/.test(rawSeq))
        errs.push('Sequence contains invalid characters — allowed: A C G U (RNA uppercase) · a c g t (DNA lowercase)')
    }

    if (!struct)
      errs.push('Structure is required')

    if (chiVal.trim() && seqVal.trim() && chiVal.trim().length !== seqVal.trim().length)
      errs.push(`Chi length (${chiVal.trim().length}) must match sequence length (${seqVal.trim().length})`)

    return errs
  }, [seqVal, structVal, chiVal])

  const currentErrors = validationErrors()
  const hasErrors = currentErrors.length > 0

  // Mark a field as touched when user leaves it
  const markTouched = useCallback((field) => {
    setTouched(prev => new Set([...prev, field]))
  }, [])

  // Per-field errors — only shown for touched fields
  const seqError = touched.has('seq') ? (() => {
    const raw = seqVal.trim()
    if (!raw) return 'Sequence is required'
    if (raw.length < 4) return 'Sequence is too short (minimum 4 nucleotides)'
    if (/T/.test(raw)) return "Sequence contains uppercase 'T' — RNA residues use 'U'"
    if (/u/.test(raw)) return "Sequence contains lowercase 'u' — invalid: use 't' for DNA or 'U' for RNA"
    if (!/^[ACGUacgt]+$/.test(raw)) return 'Sequence contains invalid characters'
    return null
  })() : null

  const structError = touched.has('struct') ? (() => {
    const struct = structVal.trim()
    if (!struct) return 'Structure is required'
    return null
  })() : null

  // Length mismatch — shown once under Step 1 card when either field was touched
  const lengthMismatchError = (touched.has('seq') || touched.has('struct'))
    && seqVal.trim() && structVal.trim()
    && seqVal.trim().length !== structVal.trim().length
    ? `Sequence and structure length mismatch (seq: ${seqVal.trim().length}, struct: ${structVal.trim().length})`
    : null

  const nameError = touched.has('name') && !nameVal.trim()
    ? 'Structure name is required' : null

  const chiError = touched.has('chi') && chiVal.trim() && seqVal.trim()
    && chiVal.trim().length !== seqVal.trim().length
    ? `Chi length (${chiVal.trim().length}) must match sequence length (${seqVal.trim().length})` : null

  const sugarError = touched.has('sugar') && sugarVal.trim() && seqVal.trim()
    && sugarVal.trim().length !== seqVal.trim().length
    ? `Sugar pucker length (${sugarVal.trim().length}) must match sequence length (${seqVal.trim().length})` : null

  // Auto-fill chiVal with dots when sequence length changes
  useEffect(() => {
    const len = seqVal.trim().length
    if (len === 0) return
    setChiVal(prev => {
      if (prev && prev.length === len) return prev  // preserve user edits
      return '.'.repeat(len)
    })
  }, [seqVal])

  // Auto-generate shugarVal from sequence case: UPPERCASE→N, lowercase→S
  useEffect(() => {
    const seq = seqVal.trim()
    if (!seq) { setShugarVal(''); return }
    setShugarVal(prev => {
      if (prev && prev.length === seq.length) return prev  // preserve user edits
      return seq.split('').map(c => /[A-Z]/.test(c) ? 'N' : 'S').join('')
    })
  }, [seqVal])

  // Auto-derive path, twist length and orient length whenever the user
  // modifies sequence/structure or selects a different subtype.
  // Mirrors the algorithm in backend Domain/SilvaTopology.cs.
  useEffect(() => {
    if (!silvaData) return
    const seq = seqVal.trim().toLowerCase()
    const struct = structVal.trim()
    // Determine tetrads: prefer structure-based count, fall back to G-content.
    let n = 0
    if (struct) n = countTetrads(struct)
    else if (seq) n = Math.max(1, Math.round((seq.match(/g/g) || []).length / 4))
    if (n < 1 || n > 4) return

    const grp = silvaData.find(g => g.code === silvaGroup)
    const sub = grp?.subtypes?.find(s => s.code === subtype)
    if (!sub?.loop) return

    try {
      setPathVal(buildPath(sub.loop, n))
    } catch {
      // notation might be unparseable for some theoretical entries — leave path empty
    }

    // Auto-scale twist, rise, pucker (N-1 values) and orient (N values).
    // Only scale — never overwrite user-customised values that already match.
    const scaleNMinus1 = (prev, defaultVal) => {
      const stepsNeeded = Math.max(1, n - 1)
      const parts = prev.split(';').filter(Boolean)
      if (parts.length === stepsNeeded) return prev
      while (parts.length < stepsNeeded) parts.push(defaultVal)
      parts.length = stepsNeeded
      return parts.join(';')
    }
    setTwist(prev => {
      const stepsNeeded = Math.max(1, n - 1)
      const currentSteps = prev.split(';').filter(Boolean).length
      return currentSteps === stepsNeeded ? prev : buildDefaultTwist(n)
    })
    setRise(prev => scaleNMinus1(prev, '3.4'))
    setPucker(prev => {
      // Preserve user-set pucker values, extend with same pattern
      const stepsNeeded = Math.max(1, n - 1)
      const parts = prev.split(';').filter(Boolean)
      if (parts.length === stepsNeeded) return prev
      const defaultPucker = parts[0] || 'S'
      while (parts.length < stepsNeeded) parts.push(defaultPucker)
      parts.length = stepsNeeded
      return parts.join(';')
    })
    setOrientVal(prev => scaleOrientToTetrads(prev, n))
  }, [silvaData, silvaGroup, subtype, seqVal, structVal])

  // Derive current group/subtypes from DB data (fall back to empty while loading)
  const currentGroup    = silvaData?.find(g => g.code === silvaGroup)
  const currentSubtypes = currentGroup?.subtypes ?? []

  function handleGroupChange(g) {
    setSilvaGroup(g)
    const grp = silvaData?.find(x => x.code === g)
    if (grp?.subtypes?.length) setSubtype(grp.subtypes[0].code)
  }

  const sequence    = seqVal.trim().toLowerCase()
  const structure   = structVal.trim()
  const hasInput    = sequence.length > 0
  const tetradCount = hasInput && structure
    ? countTetrads(structure)
    : (hasInput ? Math.floor(sequence.length / 8) : null)

  const polarityLabel  = tetradCount ? defaultPolarityLabel(tetradCount) : '–'
  const detectedPucker = /[U]/.test(seqVal) ? 'N' : 'S'
  const detectedType = !hasInput ? '–'
    : (/[U]/.test(seqVal) && /[t]/.test(seqVal)) ? 'Mixed'
    : /[U]/.test(seqVal) ? 'RNA'
    : 'DNA'

  const defaults = {
    tetrads:  tetradCount ?? '–',
    type:     detectedType,
    polarity: twist === 29 ? '>>' : twist === 27 ? '<<' : twist === 19 ? '<>' : '><',
    label:    polarityLabel,
    twist:    hasInput ? twist : '–',
    rise:     hasInput ? rise : '–',
    pucker:   hasInput ? (advOpen ? pucker : detectedPucker) : '–',
  }

  async function loadExample(pdbId) {
    setParseError(null)
    const data = await fetchExampleDetail(pdbId)
    if (!data) {
      setParseError(`Could not load example '${pdbId}' from server.`)
      return
    }
    setNameVal(data.inpName ?? '')
    setSeqVal(data.sequence ?? '')
    setStructVal(data.structure ?? '')
    setChiVal(data.chi ?? '')
    setPathVal(data.path ?? '')
    setOrientVal(data.orient ?? orientFromGroup(silvaGroup))
    setTwist(String(data.twist ?? '29'))
    setRise(String(data.rise ?? '3.4'))
    setPucker(/[A-Z]/.test(data.sequence ?? '') ? 'N' : 'S')
    setIterations(data.iterations ?? 70)
    setRmLevel(data.rmLevel ?? 0)
    setIsTest(data.isTest ?? false)
  }

  function handleSubmit() {
    if (isRunning) return

    // If there are validation errors — show them and scroll to error block
    if (hasErrors) {
      setParseError(currentErrors[0])
      setTimeout(() => {
        errorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' })
      }, 50)
      return
    }

    setParseError(null)

    const name   = nameVal.trim() || 'structure'
    const seq    = seqVal.trim()  // NO toLowerCase — quadro14L distinguishes case (RNA/DNA)
    const struct = structVal.trim()

    // Path: user-supplied raw string → split on semicolons, or null if empty
    const pathList = pathVal.trim()
      ? pathVal.trim().split(';').map(s => s.trim()).filter(Boolean)
      : null

    const payload = {
      name,
      sequence:    seq,
      structure:   struct,
      chi:         chiVal.trim(),  // if empty, backend auto-generates all-dot chi
      orient:      orientVal.trim() || orientFromGroup(silvaGroup),
      rise:        rise.trim() || '3.4',
      twist:       twist.trim() || '29',
      path:        pathList,
      isTest:      isTest,
      RM_Level:    rmLevel,
      Iterations:  iterations,
      Shugar:      sugarVal.trim(), // per-residue sugar pucker for quadro14L .inp
    }

    onRun([payload])
  }

  return (
    <div>
      {/* ── Step 1: Sequence & Structure Input ── */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>
          <span className={styles.badge}>1</span>
          Sequence &amp; Structure Input
        </div>

        <div className={styles.threeLineBlock}>
          {/* Line 1 — name */}
          <div className={styles.inlineField} style={{ flexDirection: 'column', alignItems: 'stretch' }}>
            <div style={{ display: 'flex', alignItems: 'center' }}>
              <span className={styles.lineNum}>1</span>
              <span className={styles.linePrefix}>&gt;</span>
              <input
                type="text"
                className={styles.inlineInput}
                value={nameVal}
                onChange={e => setNameVal(e.target.value)}
                onBlur={() => markTouched('name')}
                placeholder="Structure name (e.g. pz74_mp_G14L)"
                spellCheck={false}
              />
            </div>
            {nameError && (
              <div style={{ fontSize: 12, color: 'var(--err-text)', padding: '3px 12px 5px 40px', display: 'flex', gap: 5 }}>
                <WarnIcon />{nameError}
              </div>
            )}
          </div>

          {/* Line 2 — sequence */}
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>2</span>
            <input
              type="text"
              className={`${styles.inlineInput} ${styles.seqFont}`}
              value={seqVal}
              onChange={e => setSeqVal(e.target.value)}
              onBlur={() => {
                markTouched('seq')
                const seq = seqVal.trim()
                if (!seq || !silvaData) return
                // Detect type: U present (uppercase) → RNA, t present (lowercase) → DNA
                // Mixed sequences: if U is present → treat as RNA, otherwise DNA
                const hasRNA = /[U]/.test(seq)   // uppercase U = RNA
                const hasDNA = /[t]/.test(seq)   // lowercase t = DNA
                if (hasRNA && !hasDNA) {
                  // Pure RNA — default UUUU (parallel), subtype 1a
                  if (silvaGroup !== 'UUUU') {
                    handleGroupChange('UUUU')
                    setSubtype('1a')
                  }
                } else if (hasDNA && !hasRNA) {
                  // Pure DNA — default UDUD (antiparallel chair), subtype 6a
                  if (silvaGroup !== 'UDUD') {
                    handleGroupChange('UDUD')
                    setSubtype('6a')
                  }
                }
                // Mixed RNA/DNA: no auto-select, leave user's choice
              }}
              placeholder="e.g. UPPERCASE AGGGUUAGGG (RNA)  · lowercase agggttaggg (DNA) · or mixed"
              spellCheck={false}
            />
            {seqError && (
              <div style={{ fontSize: 12, color: 'var(--err-text)', marginTop: 3, display: 'flex', gap: 5 }}>
                <WarnIcon />{seqError}
              </div>
            )}
          </div>

          {/* Line 3 — structure (14L format) */}
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>3</span>
            <input
              type="text"
              className={`${styles.inlineInput} ${styles.seqFont}`}
              value={structVal}
              onChange={e => setStructVal(e.target.value)}
              onBlur={() => markTouched('struct')}
              placeholder="dot-bracket + ^ markers, length must match sequence (e.g. (((^^.^^.)))....)"
              spellCheck={false}
            />
            {structError && (
              <div style={{ fontSize: 12, color: 'var(--err-text)', marginTop: 3, display: 'flex', gap: 5 }}>
                <WarnIcon />{structError}
              </div>
            )}
          </div>

        </div>

        <div className={styles.legend}>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: 'var(--text-dim)' }} />
            Line 1: Structure name
          </span>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: 'var(--teal)' }} />
            Line 2: Nucleotide sequence (UPPERCASE = RNA · lowercase = DNA)
          </span>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: '#B45309' }} />
            Line 3: 14L structure — dot-bracket + <code>^</code> (length = sequence length)
          </span>

        </div>

        {/* Length mismatch — shown once under Step 1, not duplicated under each field */}
        {lengthMismatchError && (
          <div style={{ fontSize: 12, color: 'var(--err-text)', marginTop: 8, display: 'flex', gap: 6, alignItems: 'center' }}>
            <WarnIcon />{lengthMismatchError}
          </div>
        )}

        {parseError && (
          <div ref={errorRef} style={{
            display: 'flex', alignItems: 'flex-start', gap: 8,
            margin: '12px 0 0', padding: '10px 14px',
            background: 'var(--err-bg)', border: '1px solid var(--err-border)',
            borderRadius: 'var(--r-md)', color: 'var(--err-text)',
            fontSize: 13, lineHeight: 1.5,
          }}>
            <WarnIcon />
            <div>
              <strong>{parseError}</strong>
              {currentErrors.length > 1 && (
                <ul style={{ margin: '6px 0 0', paddingLeft: 18 }}>
                  {currentErrors.slice(1).map((e, i) => <li key={i}>{e}</li>)}
                </ul>
              )}
            </div>
          </div>
        )}
      </div>

      {/* ── Mode toggle: Canonical / Non-canonical ── */}
      <div style={{ display: 'flex', gap: 0, margin: '16px 0 0', borderRadius: 'var(--r-md)', overflow: 'hidden', border: '1px solid var(--border-med)', alignSelf: 'flex-start', width: 'fit-content' }}>
        {[
          { id: 'canonical',    label: 'Canonical',     desc: 'Auto-derive parameters from Silva classification' },
          { id: 'noncanonical', label: 'Non-canonical', desc: 'Manually specify all structural parameters' },
        ].map(({ id, label, desc }) => (
          <button
            key={id}
            onClick={() => setMode(id)}
            title={desc}
            style={{
              padding: '8px 20px',
              fontSize: 13,
              fontWeight: 600,
              fontFamily: 'var(--sans)',
              border: 'none',
              cursor: 'pointer',
              background: mode === id ? 'var(--teal)' : 'var(--surface)',
              color:      mode === id ? 'white'       : 'var(--text-dim)',
              transition: 'background 0.15s, color 0.15s',
            }}
          >
            {label}
          </button>
        ))}
      </div>

      {/* ── Non-canonical examples — small load buttons above Advanced ── */}
      {mode === 'noncanonical' && nonCanonical.length > 0 && (
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', margin: '8px 0 0' }}>
          <span style={{ fontSize: 12, color: 'var(--text-dim)', alignSelf: 'center' }}>Load example:</span>
          {nonCanonical.map(ex => (
            <button
              key={ex.pdbId}
              onClick={() => loadExample(ex.pdbId)}
              title={ex.note}
              style={{
                padding: '4px 12px', fontSize: 12, fontFamily: 'var(--mono)',
                fontWeight: 600, cursor: 'pointer',
                border: '1px solid var(--border-med)', borderRadius: 'var(--r-sm)',
                background: 'var(--surface)', color: 'var(--teal-dark)',
                transition: 'background 0.12s, border-color 0.12s',
              }}
              onMouseOver={e => { e.currentTarget.style.background = 'var(--teal-light)'; e.currentTarget.style.borderColor = 'var(--teal)' }}
              onMouseOut={e => { e.currentTarget.style.background = 'var(--surface)'; e.currentTarget.style.borderColor = 'var(--border-med)' }}
            >
              {ex.pdbId.toUpperCase()} · {ex.tetrads}T
            </button>
          ))}
        </div>
      )}

      {/* ── Step 2: Silva Loop Classification (canonical only) ── */}
      {mode === 'canonical' && <div className={styles.card}>
        <div className={styles.cardTitle}>
          <span className={styles.badge}>2</span>
          Silva Loop Classification
        </div>

        <div className={styles.row}>
          <div className={styles.label}>
            Strand topology
            <small>Select G4 group</small>
          </div>
          <div className={styles.control}>
            <div className={styles.silvaGrid}>
              {(silvaData ?? []).map(g => (
                <button
                  key={g.code}
                  className={`${styles.silvaBtn} ${silvaGroup === g.code ? styles.silvaBtnActive : ''}`}
                  onClick={() => handleGroupChange(g.code)}
                  title={`Group ${g.groupNumber} · ${g.name}`}
                >
                  <Beads code={g.code} active={silvaGroup === g.code} />
                  <span className={styles.silvaBtnLabel}>{g.code}</span>
                </button>
              ))}
            </div>
            <div className={styles.silvaGroupInfo}>
              {currentGroup ? (
                <>
                  <span className={styles.groupBadge}>Group {currentGroup.groupNumber}</span>
                  <span className={styles.groupName}>{currentGroup.name}</span>
                  <span className={styles.groupGroove}>groove: <code>{currentGroup.groove}</code></span>
                </>
              ) : silvaError ? (
                <span style={{ color: 'var(--err-text)', fontSize: 13 }}>Failed to load classification data</span>
              ) : (
                <span style={{ color: 'var(--text-dim)', fontSize: 13 }}>Loading…</span>
              )}
            </div>
          </div>
        </div>

        <div className={styles.row}>
          <div className={styles.label}>
            Loop subtype
            <small>Subtypes for <code className={styles.inlineCode}>{silvaGroup}</code></small>
          </div>
          <div className={styles.control}>
            <div className={styles.subtypeList}>
              {currentSubtypes.map(s => (
                <div
                  key={s.code}
                  className={`${styles.subRow} ${subtype === s.code ? styles.subRowActive : ''}`}
                  onClick={() => setSubtype(s.code)}
                >
                  <span className={`${styles.subDot} ${subtype === s.code ? styles.subDotActive : ''}`} />
                  <code className={`${styles.subCode} ${subtype === s.code ? styles.subCodeActive : ''}`}>{s.code}</code>
                  <code className={styles.subLoop}>{s.loop}</code>
                  <span className={styles.subSilva}>{s.silva}</span>
                  <span className={styles.subOnz} data-onz={s.onz}>{s.onz}</span>
                  {s.note && <span className={styles.subNote}>{s.note}</span>}
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Examples browser */}
        <div className={styles.row}>
          <div className={styles.label}>
            PDB examples
            <small>Known structures for {subtype}</small>
          </div>
          <div className={styles.control}>
            {(() => {
              const currentSub = currentSubtypes.find(s => s.code === subtype)
              const examples = currentSub?.examples ?? []
              return examples.length === 0 ? (
                <span className={styles.noExamples}>No deposited examples for subtype {subtype}</span>
              ) : (
                <div className={styles.examplesList}>
                  {examples.map(ex => (
                    <button
                      key={ex.pdbId}
                      className={styles.exampleBtn}
                      onClick={() => loadExample(ex.pdbId)}
                      title={`Load ${ex.pdbId} — ${ex.note}`}
                    >
                      <span className={styles.exPdb}>{ex.pdbId.toUpperCase().replace(/^_/, '')}</span>
                      <span className={styles.exTetrads}>{ex.tetrads}T</span>
                      <span className={styles.exNote}>{ex.note}</span>
                      {ex.isTheoretical && <span className={styles.subNote}>theoretical</span>}
                      <span className={styles.exArrow}>→ Load</span>
                    </button>
                  ))}
                </div>
              )
            })()}
          </div>
        </div>
      </div>

      }

      {/* ── Step 3: Computed Default Parameters (canonical only) ── */}
      {mode === 'canonical' && <div className={styles.card}>
        <div className={styles.cardTitle}>
          <span className={styles.badge}>3</span>
          Computed Default Parameters
        </div>
        <div className={styles.defaultsBar}>
          {[
            { lbl: 'Tetrads',  val: defaults.tetrads,                                        active: true  },
            { lbl: 'Type',     val: defaults.type,                                            active: false },
            { lbl: 'Polarity', val: defaults.polarity,                                        active: true  },
            { lbl: 'Label',    val: defaults.label,                                           active: false },
            { lbl: 'Twist',    val: defaults.twist !== '–' ? `${defaults.twist}°` : '–',     active: true  },
            { lbl: 'Rise',     val: defaults.rise  !== '–' ? `${defaults.rise} Å` : '–',     active: false },
            { lbl: 'Pucker',   val: defaults.pucker,                                          active: true  },
          ].map(({ lbl, val, active }) => (
            <div key={lbl} className={`${styles.defCell} ${active ? styles.defActive : ''}`}>
              <div className={styles.defLbl}>{lbl}</div>
              <div className={styles.defVal}>{val}</div>
            </div>
          ))}
        </div>
        <p className={styles.defaultsNote}>
          Polarity defaults: 2T→RL · 3T→RLL · 4T→RLRL · 5T+→alternating
        </p>
      </div>

      }

      {/* ── Step 4: Advanced Parameters ── */}
      {/* In non-canonical mode: always expanded and not collapsible */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>
          <span className={styles.badge}>{mode === 'canonical' ? '4' : '2'}</span>
          {mode === 'canonical' ? 'Advanced Parameters' : 'Structural Parameters'}
          {mode === 'canonical' && (
            <>
              <span className={styles.cardTitleNote}>Optional — overrides computed defaults</span>
              <button
                className={`${styles.toggleBtn} ${advOpen ? styles.toggleBtnOpen : ''}`}
                onClick={() => setAdvOpen(v => !v)}
              >
                {advOpen ? 'Collapse' : 'Expand'}
              </button>
            </>
          )}
        </div>

        {(advOpen || mode === 'noncanonical') && (
          <div>
            {/* Orient */}
            <div className={styles.row}>
              <div className={styles.label}>
                Strand orientation
                <small>orient field in .inp (auto-set from topology)</small>
              </div>
              <input
                type="text"
                style={{ maxWidth: 220 }}
                value={orientVal}
                onChange={e => setOrientVal(e.target.value)}
                placeholder="e.g. A+;B- or A-;B-"
                spellCheck={false}
              />
            </div>

            {/* Chi — glycosidic bond conformation */}
            <div className={styles.row}>
              <div className={styles.label}>Chi (χ)<small>S=syn · N/A=anti · .=default · length must match sequence</small></div>
              <div style={{ width: '100%' }}>
                <input type="text"
                  style={{ width: '100%', fontFamily: 'var(--mono)', fontSize: 13 }}
                  value={chiVal} onChange={e => setChiVal(e.target.value)} onBlur={() => markTouched('chi')}
                  placeholder={`${seqVal.trim().length || 0} chars — e.g. S.....S.....SS.......SS.`}
                  spellCheck={false} />
                {chiError && (
                  <div style={{ fontSize: 12, color: 'var(--err-text)', marginTop: 4, display: 'flex', gap: 5 }}>
                    <WarnIcon />{chiError}
                  </div>
                )}
              </div>
            </div>

            {/* Path — auto-derived from Silva loop notation, editable */}
            <div className={styles.row}>
              <div className={styles.label}>
                Tetrad path
                <small>auto-derived from subtype loop + tetrad count</small>
              </div>
              <input
                type="text"
                className={styles.seqFont}
                style={{ width: '100%', fontFamily: 'var(--mono)' }}
                value={pathVal}
                onChange={e => setPathVal(e.target.value)}
                placeholder="e.g. A1;B1;C1;C2;B2;A2;C3;B3;A3;A4;B4;C4"
                spellCheck={false}
              />
            </div>

            {/* Helical twist — dynamic, one input per inter-tetrad transition */}
            <div className={styles.row}>
              <div className={styles.label}>
                Helical twist
                <small>one value per inter-tetrad transition (in degrees)</small>
              </div>
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 12 }}>
                {(() => {
                  const tetrads = Math.max(1, countTetrads(structVal.trim()) || 1)
                  const steps   = Math.max(1, tetrads - 1)
                  const parts   = twist.split(';').map(s => s.trim())
                  while (parts.length < steps) parts.push('29')
                  if (parts.length > steps) parts.length = steps

                  return parts.map((val, i) => (
                    <div key={i} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                      <span style={{ fontSize: 11, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>
                        T{i + 1}→T{i + 2}
                      </span>
                      <input
                        type="text"
                        value={val}
                        onChange={e => {
                          const next = [...parts]
                          next[i] = e.target.value.replace(/[^0-9.]/g, '')
                          setTwist(next.join(';'))
                        }}
                        placeholder="29"
                        style={{ width: 72, fontFamily: 'var(--mono)', textAlign: 'center' }}
                      />
                    </div>
                  ))
                })()}
                <span style={{ fontSize: 12, color: 'var(--text-dim)', marginBottom: 8 }}>°</span>
              </div>
            </div>

            {/* Rise — dynamic per inter-tetrad step */}
            <div className={styles.row}>
              <div className={styles.label}>Helical rise<small>axial translation per step (Å)</small></div>
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 12 }}>
                {(() => {
                  const tetrads = Math.max(1, countTetrads(structVal.trim()) || 1)
                  const steps   = Math.max(1, tetrads - 1)
                  const parts   = rise.split(';').map(s => s.trim())
                  while (parts.length < steps) parts.push('3.4')
                  if (parts.length > steps) parts.length = steps
                  return parts.map((val, i) => (
                    <div key={i} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                      <span style={{ fontSize: 11, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>
                        T{i+1}→T{i+2}
                      </span>
                      <input type="text" value={val}
                        onChange={e => { const n=[...parts]; n[i]=e.target.value.replace(/[^0-9.]/g,''); setRise(n.join(';')) }}
                        placeholder="3.4" style={{ width: 72, fontFamily: 'var(--mono)', textAlign: 'center' }} />
                    </div>
                  ))
                })()}
                <span style={{ fontSize: 12, color: 'var(--text-dim)', marginBottom: 8 }}>Å</span>
              </div>
            </div>

            {/* Sugar pucker — per residue (like Chi), N=North/RNA, S=South/DNA */}
            <div className={styles.rowTop}>
              <div className={styles.label}>
                Sugar pucker
                <small>N = North / RNA · S = South / DNA · . = default · one char per residue</small>
              </div>
              <div style={{ width: '100%' }}>
                <input type="text"
                  style={{ width: '100%', fontFamily: 'var(--mono)', fontSize: 13 }}
                  value={sugarVal}
                  onChange={e => setShugarVal(e.target.value)}
                  onBlur={() => markTouched('sugar')}
                  placeholder={`${seqVal.trim().length || 0} chars — auto-generated (UPPERCASE=RNA→N, lowercase=DNA→S)`}
                  spellCheck={false} />
                {sugarError && (
                  <div style={{ fontSize: 12, color: 'var(--err-text)', marginTop: 4, display: 'flex', gap: 5 }}>
                    <WarnIcon />{sugarError}
                  </div>
                )}
              </div>
            </div>


          </div>
        )}
      </div>

      {/* Submit */}
      <div className={styles.submitArea} style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8 }}>

        <button
          className={styles.btnRun}
          onClick={handleSubmit}
          disabled={isRunning}
          style={{ opacity: hasErrors ? 0.45 : 1, cursor: hasErrors ? 'not-allowed' : 'pointer' }}
          title={hasErrors ? currentErrors[0] : undefined}
        >
          {isRunning
            ? <><SpinIcon /> Computing…</>
            : <><PlayIcon /> Submit (RUN)</>
          }
        </button>
      </div>
    </div>
  )
}

/* ── Small helper components ─────────────────────────────────────────────── */

function Beads({ code, active }) {
  return (
    <div style={{ display: 'flex', gap: 3, alignItems: 'center' }}>
      {code.split('').map((c, i) => (
        <span key={i} style={{
          width: 10, height: 10, borderRadius: '50%',
          border: `1px solid ${c === 'U' ? 'var(--teal-dark)' : 'var(--border-med)'}`,
          background: c === 'U' ? 'var(--teal)' : (active ? '#9FE1CB' : 'var(--surface2)'),
          display: 'inline-block',
        }} />
      ))}
    </div>
  )
}

const PlayIcon = () => (
  <svg width="14" height="14" viewBox="0 0 14 14" fill="currentColor">
    <polygon points="2,1 13,7 2,13"/>
  </svg>
)

const SpinIcon = () => (
  <svg width="14" height="14" viewBox="0 0 14 14" fill="none" style={{ animation: 'spin .8s linear infinite' }}>
    <circle cx="7" cy="7" r="5" stroke="currentColor" strokeWidth="1.5" strokeDasharray="20" strokeDashoffset="5"/>
    <style>{'@keyframes spin{to{transform:rotate(360deg)}}'}</style>
  </svg>
)

const WarnIcon = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" style={{ flexShrink: 0 }}>
    <path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/>
    <line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
  </svg>
)
