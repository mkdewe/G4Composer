import { useState, useEffect } from 'react'
import {
  parseInputLines,
  validateEntry,
  defaultPolarityLabel,
  countTetrads,
  orientFromGroup,
} from '../utils/sequenceParser.js'
import { fetchSilvaGroups, fetchExampleDetail } from '../services/apiService.js'
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
  }, [])

  const [nameVal,       setNameVal]       = useState('')
  const [seqVal,        setSeqVal]        = useState('')
  const [structVal,     setStructVal]     = useState('')
  const [pathVal,       setPathVal]       = useState('')
  const [chiVal,        setChiVal]        = useState('')
  const [orientVal,     setOrientVal]     = useState('A+;B-')
  const [silvaGroup,    setSilvaGroup]    = useState('UDUD')
  const [subtype,       setSubtype]       = useState('6a')
  const [advOpen,       setAdvOpen]       = useState(false)
  const [twist,         setTwist]         = useState('29')
  const [rise,          setRise]          = useState(3.4)
  const [pucker,        setPucker]        = useState('S')
  const [iterations,    setIterations]    = useState(100)
  const [rmLevel,       setRmLevel]       = useState(0)
  const [isTest,        setIsTest]        = useState(false)
  const [parseError,    setParseError]    = useState(null)

  const isRunning = runState === 'running'

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

    // Auto-scale twist (N-1 values) and orient (N values) to the current tetrad count.
    // Only scale — never overwrite user-customised values that already match.
    setTwist(prev => {
      const stepsNeeded = Math.max(1, n - 1)
      const currentSteps = prev.split(';').filter(Boolean).length
      return currentSteps === stepsNeeded ? prev : buildDefaultTwist(n)
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
  const detectedPucker = /[A-Z]/.test(seqVal) ? 'N' : 'S'

  const defaults = {
    tetrads:  tetradCount ?? '–',
    type:     hasInput ? (detectedPucker === 'N' ? 'RNA' : 'DNA') : '–',
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
    setRise(data.rise ?? 3.4)
    setPucker(/[A-Z]/.test(data.sequence ?? '') ? 'N' : 'S')
    setIterations(data.iterations ?? 70)
    setRmLevel(data.rmLevel ?? 0)
    setIsTest(data.isTest ?? false)
  }

  function handleSubmit() {
    if (isRunning) return

    const name   = nameVal.trim() || 'structure'
    const seq    = seqVal.trim().toLowerCase()
    const struct = structVal.trim()

    if (!seq) { setParseError('Sequence is required'); return }
    if (seq.length < 4) { setParseError('Sequence is too short (minimum 4 nucleotides)'); return }
    if (!/^[acgutrykmbdhvnswACGUTRYKMBDHVNSW]+$/.test(seq)) {
      setParseError('Sequence contains invalid characters (allowed: a c g u t — lowercase)'); return
    }
    if (!struct) { setParseError('Structure is required'); return }

    setParseError(null)

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
      rise:        rise,
      twist:       twist.trim() || '29',
      path:        pathList,
      isTest:      isTest,
      RM_Level:    rmLevel,
      Iterations:  iterations,
      sugarPucker: advOpen ? pucker : detectedPucker,
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
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>1</span>
            <span className={styles.linePrefix}>&gt;</span>
            <input
              type="text"
              className={styles.inlineInput}
              value={nameVal}
              onChange={e => setNameVal(e.target.value)}
              placeholder="Structure name (e.g. pz74_mp_G14L)"
              spellCheck={false}
            />
          </div>

          {/* Line 2 — sequence */}
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>2</span>
            <input
              type="text"
              className={`${styles.inlineInput} ${styles.seqFont}`}
              value={seqVal}
              onChange={e => setSeqVal(e.target.value)}
              placeholder="nucleotide sequence — lowercase (e.g. agggttagggttaggg)"
              spellCheck={false}
            />
          </div>

          {/* Line 3 — structure (14L format) */}
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>3</span>
            <input
              type="text"
              className={`${styles.inlineInput} ${styles.seqFont}`}
              value={structVal}
              onChange={e => setStructVal(e.target.value)}
              placeholder="dot-bracket + ^ markers, length must match sequence (e.g. (((^^.^^.)))....)"
              spellCheck={false}
            />
          </div>

        </div>

        <div className={styles.legend}>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: 'var(--text-dim)' }} />
            Line 1: Structure name
          </span>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: 'var(--teal)' }} />
            Line 2: Nucleotide sequence (lowercase)
          </span>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: '#B45309' }} />
            Line 3: 14L structure — dot-bracket + <code>^</code> (length = sequence length)
          </span>

        </div>

        {parseError && (
          <div className={styles.errorMsg}><WarnIcon /> {parseError}</div>
        )}
      </div>

      {/* ── Step 2: Silva Loop Classification ── */}
      <div className={styles.card}>
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

      {/* ── Step 3: Computed Default Parameters ── */}
      <div className={styles.card}>
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

      {/* ── Step 4: Advanced Parameters ── */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>
          <span className={styles.badge}>4</span>
          Advanced Parameters
          <span className={styles.cardTitleNote}>Optional — overrides computed defaults</span>
          <button
            className={`${styles.toggleBtn} ${advOpen ? styles.toggleBtnOpen : ''}`}
            onClick={() => setAdvOpen(v => !v)}
          >
            {advOpen ? 'Collapse' : 'Expand'}
          </button>
        </div>

        {advOpen && (
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

            <div className={styles.row}>
              <div className={styles.label}>Rise per residue<small>Axial translation (Å)</small></div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <input type="number" value={rise} min="2.0" max="6.0" step="0.1" style={{ width: 100 }}
                  onChange={e => setRise(+e.target.value)} />
                <span style={{ fontSize: 14, color: 'var(--text-dim)' }}>Å</span>
              </div>
            </div>

            <div className={styles.row}>
              <div className={styles.label}>Sugar pucker<small>Ribose conformation</small></div>
              <div className={styles.puckerBtns}>
                {[['S',"C2′-endo / DNA"],['N',"C3′-endo / RNA"]].map(([sym, label]) => (
                  <button
                    key={sym}
                    className={`${styles.puckBtn} ${pucker === sym ? styles.puckBtnActive : ''}`}
                    onClick={() => setPucker(sym)}
                  >
                    {sym}
                    <span className={styles.puckSub}>{label}</span>
                  </button>
                ))}
              </div>
            </div>

            {/* Iterations */}
            <div className={styles.row}>
              <div className={styles.label}>Iterations<small>CYANA iteration count</small></div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <input type="number" value={iterations} min="1" max="10000" step="10" style={{ width: 100 }}
                  onChange={e => setIterations(Math.max(1, +e.target.value))} />
                <span style={{ fontSize: 14, color: 'var(--text-dim)' }}>cycles</span>
              </div>
            </div>

            {/* RM Level */}
            <div className={styles.row}>
              <div className={styles.label}>RM Level<small>rm_level in .inp (0 = skip)</small></div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <input type="number" value={rmLevel} min="0" max="10" step="1" style={{ width: 100 }}
                  onChange={e => setRmLevel(Math.max(0, +e.target.value))} />
              </div>
            </div>

            {/* Test mode */}
            <div className={styles.row}>
              <div className={styles.label}>
                Test mode
                <small>test y/n in .inp — skips full CYANA run</small>
              </div>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 14 }}>
                <input
                  type="checkbox"
                  checked={isTest}
                  onChange={e => setIsTest(e.target.checked)}
                  style={{ width: 'auto' }}
                />
                Enable test mode (faster, no real structure output)
              </label>
            </div>
          </div>
        )}
      </div>

      {/* Submit */}
      <div className={styles.submitArea}>
        <button className={styles.btnRun} onClick={handleSubmit} disabled={isRunning || !hasInput}>
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
