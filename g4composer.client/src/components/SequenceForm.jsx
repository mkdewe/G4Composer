import { useState, useRef } from 'react'
import { parseInputLines, validateEntry, defaultPolarityLabel, countTetrads } from '../utils/sequenceParser.js'
import styles from './SequenceForm.module.css'

// ── Silva classification data ─────────────────────────────────────────────────
// Source: Webba da Silva 2007, Karsisiotis 2013, Dvorkin 2018
const SILVA_DATA = {
  UUDD: {
    group: 'I', name: 'antiparallel: basket2', groove: 'mwmn',
    subtypes: [
      { code: '3a', loop: '-P-Lw-P',   silva: '-(plp)',  onz: 'O' },
      { code: '5a', loop: '-PD+P',     silva: '-pd+p',   onz: 'N', note: 'RNA only' },
      { code: '8b', loop: '+Ln+P+Lw',  silva: '+(lpl)',  onz: 'O' },
      { code: '11b',loop: '+LnD-Lw',   silva: '+ld-l',   onz: 'N' },
      { code: '12a',loop: 'D-PD',      silva: 'd-pd',    onz: 'Z' },
    ],
  },
  UDUD: {
    group: 'II', name: 'antiparallel: chair', groove: 'wnwn',
    subtypes: [
      { code: '6a', loop: '-Lw-Ln-Lw', silva: '-(lll)', onz: 'O' },
      { code: '6b', loop: '+Ln+Lw+Ln', silva: '+(lll)', onz: 'O' },
    ],
  },
  UDUU: {
    group: 'III', name: 'hybrid3', groove: 'wnmm',
    subtypes: [
      { code: '2b', loop: '+P+P+Ln',   silva: '+(ppl)', onz: 'O' },
      { code: '7a', loop: '-Lw-Ln-P',  silva: '-(llp)', onz: 'O' },
      { code: '10b',loop: '+PD-Ln',    silva: '-pd-l',  onz: 'N' },
      { code: '13a',loop: '-LwD+P',    silva: '-ldp',   onz: 'N' },
    ],
  },
  UUUD: {
    group: 'IV', name: 'hybrid2', groove: 'mmwn',
    subtypes: [
      { code: '2a', loop: '-P-P-Lw',   silva: '-(ppl)', onz: 'O' },
      { code: '7b', loop: '+Ln+Lw+P',  silva: '+l-l-p', onz: 'O' },
      { code: '10a',loop: '-PD+Lw',    silva: '-pd+l',  onz: 'N' },
      { code: '13b',loop: '+LnD-P',    silva: '+ld-p',  onz: 'N' },
    ],
  },
  UUDU: {
    group: 'V', name: 'hybrid1', groove: 'mwnm',
    subtypes: [
      { code: '9a', loop: '-P-Lw-Ln',  silva: '-(pll)', onz: 'O' },
      { code: '9b', loop: '+P+Ln+Lw',  silva: '+(pll)', onz: 'O' },
    ],
  },
  UDDU: {
    group: 'VI', name: 'antiparallel: basket', groove: 'wmnm',
    subtypes: [
      { code: '3b', loop: '+P+Ln+P',   silva: '+(plp)', onz: 'O' },
      { code: '5b', loop: '+PD-P',     silva: '+pd-p',  onz: 'N' },
      { code: '8a', loop: '-Lw-P-Ln',  silva: '-(lpl)', onz: 'O' },
      { code: '11a',loop: '-LwD+Ln',   silva: '-ld+l',  onz: 'N' },
      { code: '12b',loop: 'D+PD',      silva: 'dpd',    onz: 'Z' },
    ],
  },
  UDDD: {
    group: 'VII', name: 'hybrid4', groove: 'wmmn',
    subtypes: [
      { code: '4a', loop: '-Lw-P-P',   silva: '-(lpp)', onz: 'O' },
      { code: '4b', loop: '+Ln+P+P',   silva: '+(lpp)', onz: 'O' },
    ],
  },
  UUUU: {
    group: 'VIII', name: 'parallel', groove: 'mmmm',
    subtypes: [
      { code: '1a', loop: '-P-P-P',    silva: '-(ppp)', onz: 'O' },
      { code: '1b', loop: '+P+P+P',    silva: '+(ppp)', onz: 'O', note: 'left-handed only' },
    ],
  },
}

const SILVA_GROUPS = Object.keys(SILVA_DATA)

// ── Example database ──────────────────────────────────────────────────────────
// Source: G4_unimolecular_all (DSSR G4DB). TODO: fill in full .inp data per example.
const EXAMPLES_DB = {
  // Group I — UUDD
  '5a':  [{ pdbId: '8k7w', tetrads: 2, note: 'Spinach aptamer (bulges in D strand)' }],
  '8b':  [{ pdbId: '2mbj', tetrads: 3, note: 'Classic UUDD 3-tetrad basket2' }],
  '11b': [{ pdbId: '2kow', tetrads: 3, note: 'UUDD antiparallel basket2' }],
  // Group II — UDUD
  '6a':  [{ pdbId: '1hap', tetrads: 2, note: 'Oxytricha telomeric G4' },
          { pdbId: '1hut', tetrads: 2, note: 'Tet repeat d(T4G4)' }],
  '6b':  [{ pdbId: '1qdh', tetrads: 2, note: 'Human telomere antiparallel' },
          { pdbId: '148d', tetrads: 4, note: 'Antiparallel chair 4-tetrad' }],
  // Group III — UDUU
  '7a':  [{ pdbId: '2mfu', tetrads: 2, note: 'UDUU hybrid3' },
          { pdbId: '186d', tetrads: 3, note: 'Intramolecular G4 hybrid3' }],
  // Group IV — UUUD
  '2a':  [{ pdbId: '6up0', tetrads: 2, note: 'Mango-III aptamer' }],
  '10a': [{ pdbId: '5ov2', tetrads: 3, note: 'UUUD hybrid2 3-tetrad' }],
  // Group V — UUDU
  '9a':  [{ pdbId: '2gku', tetrads: 3, note: 'UUDU hybrid1 3-tetrad' },
          { pdbId: '2may', tetrads: 3, note: 'Human telomere hybrid-1' }],
  // Group VI — UDDU
  '11a': [{ pdbId: '143d', tetrads: 2, note: 'Human telomere basket' },
          { pdbId: '2m91', tetrads: 2, note: 'UDDU antiparallel basket' }],
  '12b': [{ pdbId: '1i34', tetrads: 2, note: 'Oxytricha basket diagonal' }],
  // Group VII — UDDD
  '4b':  [{ pdbId: '7zeo', tetrads: 2, note: 'UDDD hybrid4' }],
  // Group VIII — UUUU
  '1a':  [{ pdbId: '1kf1', tetrads: 3, note: 'Human telomere parallel' },
          { pdbId: '2a5r', tetrads: 2, note: 'TBA parallel 2-tetrad' }],
}

// Placeholder .inp data for each example — TODO: fill with real coordinates/paths
function getExampleInp(pdbId, subtypeCode) {
  const PLACEHOLDER_INPUTS = {
    '1hap': {
      name: '1hap_js12B', sequence: 'ggttggtgtggttgg',
      structure: 'AB..BA...AB..BA', chi: 'S...S....S...S.',
      orient: 'A+;B-', rise: 3.4, twist: 19,
      path: ['A1','B1','B4','A4','A3','B3','B2','A2'],
      isTest: true, RM_Level: 5, Iterations: 50,
    },
    '1kf1': {
      name: '1kf1_parallel', sequence: 'agggttagggttagggttaggg',
      structure: 'AAAA....AAAA....AAAA....AAAA', chi: '',
      orient: 'A+;B+;C+;D+', rise: 3.4, twist: 29,
      path: ['A1','B1','C1','D1','A2','B2','C2','D2','A3','B3','C3','D3'],
      isTest: true, RM_Level: 5, Iterations: 100,
    },
    '143d': {
      name: '143d_basket', sequence: 'agggttagggttagggttaggg',
      structure: 'AB..BA...AB..BA', chi: '',
      orient: 'A+;B-', rise: 3.4, twist: 19,
      path: ['A1','B1','B4','A4','A3','B3','B2','A2'],
      isTest: true, RM_Level: 5, Iterations: 100,
    },
  }
  // Return placeholder or generic template
  const data = PLACEHOLDER_INPUTS[pdbId]
  if (data) return data
  return {
    name: `${pdbId}_${subtypeCode}`,
    sequence: 'ggttggtgtggttgg',
    structure: 'AB..BA...AB..BA',
    chi: '', orient: 'A+;B-', rise: 3.4, twist: 29,
    path: ['A1','B1','B4','A4'],
    isTest: true, RM_Level: 5, Iterations: 100,
  }
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function SequenceForm({ onRun, runState }) {
  // Textarea is empty by default — placeholder shows watermark text
  const [nameVal, setNameVal]       = useState('')
  const [seqVal, setSeqVal]         = useState('')
  const [structVal, setStructVal]   = useState('')

  const [silvaGroup, setSilvaGroup] = useState('UDDU')
  const [subtype, setSubtype]       = useState('11a')
  const [advOpen, setAdvOpen]       = useState(false)
  const [twist, setTwist]           = useState(29)
  const [rise, setRise]             = useState(3.4)
  const [pucker, setPucker]         = useState('S')
  const [parseError, setParseError] = useState(null)
  const [showExamples, setShowExamples] = useState(false)

  const isRunning = runState === 'running'

  // Current silva group subtypes
  const currentGroup = SILVA_DATA[silvaGroup]
  const currentSubtypes = currentGroup.subtypes

  // Auto-select first subtype when group changes
  function handleGroupChange(g) {
    setSilvaGroup(g)
    setSubtype(SILVA_DATA[g].subtypes[0].code)
    setShowExamples(false)
  }

  // Computed values from current input
  const sequence  = seqVal.trim().toLowerCase()
  const structure = structVal.trim()
  const hasInput  = sequence.length > 0

  const tetradCount = hasInput && structure
    ? countTetrads(structure)
    : (hasInput ? Math.floor(sequence.length / 4) : null)

  const polarityLabel = tetradCount ? defaultPolarityLabel(tetradCount) : '–'
  const detectedPucker = /[A-Z]/.test(seqVal) ? 'N' : 'S'

  const defaults = {
    tetrads:  tetradCount ?? '–',
    type:     hasInput ? (detectedPucker === 'N' ? 'RNA' : 'DNA') : '–',
    polarity: twist === 29 ? '>>' : twist === 27 ? '<<' : twist === 19 ? '<>' : '><',
    label:    tetradCount ? defaultPolarityLabel(tetradCount) : '–',
    twist:    hasInput ? twist : '–',
    rise:     hasInput ? rise : '–',
    pucker:   hasInput ? detectedPucker : '–',
  }

  // Load an example into form
  function loadExample(pdbId, subtypeCode) {
    const data = getExampleInp(pdbId, subtypeCode)
    setNameVal(data.name)
    setSeqVal(data.sequence)
    setStructVal(data.structure)
    setTwist(data.twist)
    setRise(data.rise)
    setPucker(/[A-Z]/.test(data.sequence) ? 'N' : 'S')
    setShowExamples(false)
    setParseError(null)
  }

  // Build payload and run
  function handleSubmit() {
    if (isRunning) return
    const name = nameVal.trim() || 'structure'
    const seq  = seqVal.trim()
    const str  = structVal.trim()

    if (!seq) { setParseError('Sequence is required'); return }
    if (seq.length < 4) { setParseError('Sequence is too short (minimum 4 nucleotides)'); return }
    if (!/^[ACGUTacgut]+$/.test(seq)) { setParseError('Invalid characters in sequence'); return }

    setParseError(null)

    // Build path from structure if it contains strand labels
    let path = null
    const strandLabels = str.match(/[A-Z]\d+/g)
    if (strandLabels && strandLabels.length > 0) {
      path = strandLabels
    }

    const payload = {
      name,
      sequence:   seq.toLowerCase(),
      structure:  str,
      chi:        '',
      orient:     'A+;B+',
      rise:       advOpen ? rise  : 3.4,
      twist:      advOpen ? twist : 29,
      path,
      isTest:     true,
      RM_Level:   5,
      Iterations: 100,
      sugarPucker: advOpen ? pucker : detectedPucker,
    }

    onRun([payload])
  }

  const examples = EXAMPLES_DB[subtype] || []

  return (
    <div>
      {/* ── Step 1: Sequence & Structure Input ── */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>
          <span className={styles.badge}>1</span>
          Sequence &amp; Structure Input
        </div>

        {/* Three separate fields — no visual dividers between them */}
        <div className={styles.threeLineBlock}>
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>1</span>
            <span className={styles.linePrefix}>&gt;</span>
            <input
              type="text"
              className={styles.inlineInput}
              value={nameVal}
              onChange={e => setNameVal(e.target.value)}
              placeholder="Structure name"
              spellCheck={false}
            />
          </div>
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>2</span>
            <input
              type="text"
              className={`${styles.inlineInput} ${styles.seqFont}`}
              value={seqVal}
              onChange={e => setSeqVal(e.target.value)}
              placeholder="nucleotide sequence (e.g. ggttggtgtggttgg)"
              spellCheck={false}
            />
          </div>
          <div className={styles.inlineField}>
            <span className={styles.lineNum}>3</span>
            <input
              type="text"
              className={`${styles.inlineInput} ${styles.seqFont}`}
              value={structVal}
              onChange={e => setStructVal(e.target.value)}
              placeholder="dot-bracket / strand structure (e.g. AB..BA...AB..BA)"
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
            Line 2: Sequence (lowercase = DNA, UPPERCASE = RNA)
          </span>
          <span className={styles.legendItem}>
            <span className={styles.ldot} style={{ background: '#B45309' }} />
            Line 3: Strand structure (A/B labels or dot-bracket)
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

        {/* Group selector */}
        <div className={styles.row}>
          <div className={styles.label}>
            Strand topology
            <small>Select G4 group</small>
          </div>
          <div className={styles.control}>
            <div className={styles.silvaGrid}>
              {SILVA_GROUPS.map(g => (
                <button
                  key={g}
                  className={`${styles.silvaBtn} ${silvaGroup === g ? styles.silvaBtnActive : ''}`}
                  onClick={() => handleGroupChange(g)}
                  title={`Group ${SILVA_DATA[g].group} · ${SILVA_DATA[g].name}`}
                >
                  <Beads code={g} active={silvaGroup === g} />
                  <span className={styles.silvaBtnLabel}>{g}</span>
                </button>
              ))}
            </div>
            <div className={styles.silvaGroupInfo}>
              <span className={styles.groupBadge}>Group {currentGroup.group}</span>
              <span className={styles.groupName}>{currentGroup.name}</span>
              <span className={styles.groupGroove}>groove: <code>{currentGroup.groove}</code></span>
            </div>
          </div>
        </div>

        {/* Subtype selector — updates dynamically per group */}
        <div className={styles.row}>
          <div className={styles.label}>
            Loop subtype
            <small>Subtypes for <code className={styles.inlineCode}>{silvaGroup}</code></small>
          </div>
          <div className={styles.control}>
            <div className={styles.subtypeList}>
              {currentSubtypes.map(({ code, loop, silva, onz, note }) => (
                <div
                  key={code}
                  className={`${styles.subRow} ${subtype === code ? styles.subRowActive : ''}`}
                  onClick={() => { setSubtype(code); setShowExamples(false) }}
                >
                  <span className={`${styles.subDot} ${subtype === code ? styles.subDotActive : ''}`} />
                  <code className={`${styles.subCode} ${subtype === code ? styles.subCodeActive : ''}`}>{code}</code>
                  <code className={styles.subLoop}>{loop}</code>
                  <span className={styles.subSilva}>{silva}</span>
                  <span className={styles.subOnz} data-onz={onz}>{onz}</span>
                  {note && <span className={styles.subNote}>{note}</span>}
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
            {examples.length === 0 ? (
              <span className={styles.noExamples}>No deposited examples for subtype {subtype}</span>
            ) : (
              <div className={styles.examplesList}>
                {examples.map(ex => (
                  <button
                    key={ex.pdbId}
                    className={styles.exampleBtn}
                    onClick={() => loadExample(ex.pdbId, subtype)}
                    title={`Load ${ex.pdbId} — ${ex.note}`}
                  >
                    <span className={styles.exPdb}>{ex.pdbId.toUpperCase()}</span>
                    <span className={styles.exTetrads}>{ex.tetrads}T</span>
                    <span className={styles.exNote}>{ex.note}</span>
                    <span className={styles.exArrow}>→ Load</span>
                  </button>
                ))}
              </div>
            )}
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
            { lbl: 'Tetrads',  val: defaults.tetrads,                             active: true  },
            { lbl: 'Type',     val: defaults.type,                                active: false },
            { lbl: 'Polarity', val: defaults.polarity,                            active: true  },
            { lbl: 'Label',    val: defaults.label,                               active: false },
            { lbl: 'Twist',    val: defaults.twist !== '–' ? `${defaults.twist}°` : '–', active: true  },
            { lbl: 'Rise',     val: defaults.rise  !== '–' ? `${defaults.rise} Å` : '–', active: false },
            { lbl: 'Pucker',   val: defaults.pucker,                              active: true  },
          ].map(({ lbl, val, active }) => (
            <div key={lbl} className={`${styles.defCell} ${active ? styles.defActive : ''}`}>
              <div className={styles.defLbl}>{lbl}</div>
              <div className={styles.defVal}>{val}</div>
            </div>
          ))}
        </div>
        <p className={styles.defaultsNote}>
          Polarity defaults: 2T→RL · 3T→RLL · 4T→RLRL · 5T+→alternating (no experimental references for 5+ tetrads)
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
            <div className={styles.row}>
              <div className={styles.label}>Polarity direction<small>Affects helical twist</small></div>
              <div className={styles.polBtns}>
                {[['>>',29,'Parallel'],['<<',27,'Antiparallel'],['<>',19,'Hybrid'],['><',37,'Mixed']].map(([sym, deg, lbl]) => (
                  <button
                    key={sym}
                    className={`${styles.polBtn} ${twist === deg ? styles.polBtnActive : ''}`}
                    onClick={() => setTwist(deg)}
                  >
                    <span className={styles.polSym}>{sym}</span>
                    <span className={styles.polDeg}>{deg}°</span>
                    <span className={styles.polLbl}>{lbl}</span>
                  </button>
                ))}
              </div>
            </div>

            <div className={styles.row}>
              <div className={styles.label}>Twist angle<small>Override computed value</small></div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <input type="number" value={twist} min="0" max="180" step="0.5" style={{ width: 100 }}
                  onChange={e => setTwist(+e.target.value)} />
                <span style={{ fontSize: 14, color: 'var(--text-dim)' }}>degrees</span>
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
                {[['S','C2′-endo / DNA'],['N','C3′-endo / RNA']].map(([sym, label]) => (
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

const PlayIcon = () => <svg width="14" height="14" viewBox="0 0 14 14" fill="currentColor"><polygon points="2,1 13,7 2,13"/></svg>
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
