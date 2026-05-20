import { useState, useEffect, useRef, useCallback } from 'react'
import {
    defaultPolarityLabel,
    countTetrads,
    orientFromGroup,
} from '../utils/sequenceParser.js'
import { fetchSilvaGroups, fetchExampleDetail, fetchNonCanonicalExamples } from '../services/apiService.js'
import { buildPath, buildDefaultTwist, scaleOrientToTetrads, buildChiFromOrient } from '../utils/silvaTopology.js'
import styles from './SequenceForm.module.css'

// ── Silva classification data ─────────────────────────────────────────────────

// ── Orient / polarity helpers ────────────────────────────────────────────────

function orientToRL(orient) {
    if (!orient) return ''
    return orient.split(';').map(o => o.slice(-1) === '+' ? 'R' : 'L').join('')
}

function updateOrientPolarity(orient, pos, rl) {
    const parts = orient ? orient.split(';') : []
    const letters = ['A', 'B', 'C', 'D']
    while (parts.length <= pos) parts.push(letters[parts.length] + '+')
    const letter = parts[pos].slice(0, -1) || letters[pos] || 'A'
    parts[pos] = letter + (rl === 'R' ? '+' : '-')
    return parts.join(';')
}

const POLARITY_TWIST = { '++': '27', '+-': '19', '-+': '37', '--': '29' }

function autoTwistFromOrient(orient) {
    const parts = orient.split(';')
    if (parts.length < 2) return '29'
    const signs = parts.map(p => p.slice(-1))
    const steps = []
    for (let i = 0; i < signs.length - 1; i++)
        steps.push(POLARITY_TWIST[signs[i] + signs[i + 1]] ?? '29')
    const unique = [...new Set(steps)]
    return unique.length === 1 ? unique[0] : steps.join(';')
}

function formatRise(val) {
    return String(val).split(';').map(s => {
        const v = parseFloat(s.trim())
        return Number.isFinite(v) ? parseFloat(v.toFixed(1)).toString() : s.trim()
    }).join(';')
}

const SILVA_DISPLAY_ORDER = { UUUU: 0, UDUD: 1, UDDU: 2, UUDD: 3, UDUU: 4, UUUD: 5, UUDU: 6, UDDD: 7 }

// ── Component ─────────────────────────────────────────────────────────────────

export default function SequenceForm({ onRun, runState }) {
    // ── Database-driven classification data ──────────────────────────────────
    const [silvaData, setSilvaData] = useState(null)   // null = loading
    const [silvaError, setSilvaError] = useState(false)

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
    const [nonCanonical, setNonCanonical] = useState([])  // non-canonical examples

    const [nameVal, setNameVal] = useState('')
    const [seqVal, setSeqVal] = useState('')
    const [structVal, setStructVal] = useState('')
    const [pathVal, setPathVal] = useState('')
    const [chiVal, setChiVal] = useState('')
    const [orientVal, setOrientVal] = useState('A+;B-')
    const [silvaGroup, setSilvaGroup] = useState('UDUD')
    const [subtype, setSubtype] = useState('6a')
    const [advOpen, setAdvOpen] = useState(false)
    const [twist, setTwist] = useState('29')
    const [rise, setRise] = useState('3.4')
    const [pucker, setPucker] = useState('S')
    const [parseError, setParseError] = useState(null)
    const [examplesOpen, setExamplesOpen] = useState(false)
    const [exFilterGroup, setExFilterGroup] = useState('UDUD')

    const isRunning = runState === 'running'
    const errorRef = useRef(null)

    // Auto-fill Chi with dots when sequence length changes (preserve user edits)
    useEffect(() => {
        const len = seqVal.trim().length
        if (!len) return
        setChiVal(prev => prev && prev.length === len ? prev : '.'.repeat(len))
    }, [seqVal])

    // Auto-generate Sugar pucker from sequence case: UPPERCASE→N, lowercase→S
    useEffect(() => {
        const seq = seqVal.trim()
        if (!seq) return
        setPucker(prev => prev && prev.length === seq.length ? prev
            : seq.split('').map(c => /[A-Z]/.test(c) ? 'N' : 'S').join(''))
    }, [seqVal])  // ref for scroll-to-error

    // ── Live validation — computed on every render ───────────────────────────
    const validationErrors = useCallback(() => {
        const errs = []
        const rawSeq = seqVal.trim()
        const seq = rawSeq.toLowerCase()
        const struct = structVal.trim()

        if (!seq) {
            errs.push('Sequence is required')
        } else if (seq.length < 4) {
            errs.push('Sequence is too short (minimum 4 nucleotides)')
        } else {
            // UPPERCASE = RNA (A,C,G,U) · lowercase = DNA (a,c,g,t)
            // Mixed RNA/DNA sequences are allowed.
            // Invalid: uppercase T, lowercase u (quadro14L ERROR 2)
            if (/T/.test(rawSeq))
                errs.push("Sequence contains uppercase 'T' — RNA residues use 'U' (uppercase)")
            if (/u/.test(rawSeq))
                errs.push("Sequence contains lowercase 'u' — use uppercase 'U' for RNA uridine (quadro14L rejects lowercase 'u')")
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
            if (currentSteps === stepsNeeded) return prev
            // Resize twist to match new tetrad count, preserving current polarity
            const parts = autoTwistFromOrient(orientVal).split(';')
            while (parts.length < stepsNeeded) parts.push(parts[parts.length - 1] ?? '29')
            parts.length = stepsNeeded
            return parts.join(';')
        })
        setRise(prev => scaleNMinus1(prev, '3.4'))
        // Sugar pucker is per-residue (like Chi) — auto-generated by seqVal useEffect, not scaled here
        setOrientVal(prev => scaleOrientToTetrads(prev, n))
    }, [silvaData, silvaGroup, subtype, seqVal, structVal, orientVal])

    // Auto-generate chi from orient + path + structure (canonical mode only).
    // Fires whenever orient, path, or structure changes.
    useEffect(() => {
        if (mode !== 'canonical') return
        if (!structVal.trim() || !pathVal.trim() || !orientVal.trim()) return
        const chi = buildChiFromOrient(structVal.trim(), pathVal.trim(), orientVal.trim())
        if (!chi) return
        setChiVal(chi)
    }, [mode, structVal, pathVal, orientVal])

    // Derive current group/subtypes from DB data (fall back to empty while loading)
    const currentGroup = silvaData?.find(g => g.code === silvaGroup)
    const currentSubtypes = currentGroup?.subtypes ?? []

    function handleGroupChange(g) {
        setSilvaGroup(g)
        const grp = silvaData?.find(x => x.code === g)
        if (grp?.subtypes?.length) setSubtype(grp.subtypes[0].code)
        // UUUU is always all-parallel (all L = all '-') — override the generic default
        if (g === 'UUUU') {
            const n = Math.max(1, countTetrads(structVal.trim()) || 2)
            setOrientVal(['A', 'B', 'C', 'D'].slice(0, n).map(l => `${l}-`).join(';'))
        }
    }

    const sequence = seqVal.trim().toLowerCase()
    const structure = structVal.trim()
    const hasInput = sequence.length > 0
    const tetradCount = hasInput && structure
        ? countTetrads(structure)
        : (hasInput ? Math.floor(sequence.length / 8) : null)

    const polarityLabel = tetradCount ? defaultPolarityLabel(tetradCount) : '–'
    const detectedType = !hasInput ? '–'
        : (/[U]/.test(seqVal) && /[t]/.test(seqVal)) ? 'RNA/DNA'
            : /[U]/.test(seqVal) ? 'RNA'
                : 'DNA'

    const rlPolarity = hasInput ? (orientToRL(orientVal) || polarityLabel) : '–'
    const defaults = {
        tetrads: tetradCount ?? '–',
        type: detectedType,
        polarity: rlPolarity,
        twist: hasInput ? twist : '–',
        rise: hasInput ? rise : '–',
    }

    async function loadExample(pdbId, groupCode = null, subtypeCode = null) {
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
        setRise(formatRise(data.rise ?? '3.4'))
        // Per-residue sugar pucker: UPPERCASE→N, lowercase→S
        const exSeq = data.sequence ?? ''
        setPucker(exSeq.split('').map(ch => /[A-Z]/.test(ch) ? 'N' : 'S').join(''))
        if (groupCode) setSilvaGroup(groupCode)
        if (subtypeCode) setSubtype(subtypeCode)
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

        const name = nameVal.trim() || 'structure'
        const seq = seqVal.trim()
        const struct = structVal.trim()

        // Path: user-supplied raw string → split on semicolons, or null if empty
        const pathList = pathVal.trim()
            ? pathVal.trim().split(';').map(s => s.trim()).filter(Boolean)
            : null

        const payload = {
            name,
            sequence: seq,
            structure: struct,
            chi: chiVal.trim(),  // if empty, backend auto-generates all-dot chi
            orient: orientVal.trim() || orientFromGroup(silvaGroup),
            rise: rise.trim() || '3.4',
            twist: twist.trim() || '29',
            path: pathList,
            Shugar: pucker,  // per-residue sugar pucker for quadro14L .inp
        }

        onRun([payload])
    }

    // Derive effective examples filter group (fall back to first available, but preserve __nc__ sentinel)
    const effectiveExGroup = exFilterGroup === '__nc__'
        ? '__nc__'
        : (silvaData?.find(g => g.code === exFilterGroup) ? exFilterGroup : (silvaData?.[0]?.code ?? exFilterGroup))

    const allExampleCount = silvaData
        ? silvaData.reduce((sum, g) => sum + g.subtypes.reduce((s2, sub) => s2 + (sub.examples?.length ?? 0), 0), nonCanonical.length)
        : null

    return (
        <div>
            {/* ── Examples Browser (collapsible, pinned at top) ── */}
            <div className={styles.card} style={{ marginBottom: 12 }}>
                <div
                    className={styles.cardTitle}
                    style={{ cursor: 'pointer', userSelect: 'none' }}
                    onClick={() => setExamplesOpen(v => !v)}
                >
                    <span style={{
                        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                        width: 22, height: 22, borderRadius: '50%', fontSize: 11, fontWeight: 700,
                        background: 'var(--teal)', color: 'white', flexShrink: 0,
                    }}>★</span>
                    Examples
                    {allExampleCount !== null && (
                        <span style={{ fontSize: 12, color: 'var(--text-dim)', fontWeight: 400, marginLeft: 4 }}>
                            — {allExampleCount} structures
                        </span>
                    )}
                    <span style={{ marginLeft: 'auto', fontSize: 12, color: 'var(--text-dim)', fontWeight: 400 }}>
                        {examplesOpen ? '▲ collapse' : '▼ expand'}
                    </span>
                </div>

                {examplesOpen && (
                    <div>
                        {/* Group tabs */}
                        <div style={{ display: 'flex', gap: 4, marginBottom: 16, paddingBottom: 12, borderBottom: '1px solid var(--border)' }}>
                            {[...(silvaData ?? [])].sort((a, b) => (SILVA_DISPLAY_ORDER[a.code] ?? 99) - (SILVA_DISPLAY_ORDER[b.code] ?? 99)).map(g => (
                                <button
                                    key={g.code}
                                    onClick={() => { setExFilterGroup(g.code); setMode('canonical'); handleGroupChange(g.code) }}
                                    style={{
                                        flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6,
                                        padding: '6px 4px', fontSize: 12, fontWeight: 600, fontFamily: 'var(--mono)',
                                        borderRadius: 'var(--r-sm)', cursor: 'pointer',
                                        border: `1px solid ${effectiveExGroup === g.code ? 'var(--teal)' : 'var(--border-med)'}`,
                                        background: effectiveExGroup === g.code ? 'var(--teal)' : 'var(--surface)',
                                        color: effectiveExGroup === g.code ? 'white' : 'var(--text-dim)',
                                        transition: 'background 0.12s, color 0.12s, border-color 0.12s',
                                    }}
                                >
                                    <Beads code={g.code} active={effectiveExGroup === g.code} />
                                    {g.code}
                                </button>
                            ))}
                            {nonCanonical.length > 0 && (
                                <button
                                    onClick={() => { setExFilterGroup('__nc__'); setMode('noncanonical') }}
                                    style={{
                                        padding: '6px 10px', fontSize: 12, fontWeight: 600,
                                        borderRadius: 'var(--r-sm)', cursor: 'pointer',
                                        border: `1px solid ${effectiveExGroup === '__nc__' ? 'var(--teal)' : 'var(--border-med)'}`,
                                        background: effectiveExGroup === '__nc__' ? 'var(--teal)' : 'var(--surface)',
                                        color: effectiveExGroup === '__nc__' ? 'white' : 'var(--text-dim)',
                                        transition: 'background 0.12s, color 0.12s, border-color 0.12s',
                                        whiteSpace: 'nowrap',
                                    }}
                                >
                                    Non-canonical
                                </button>
                            )}
                        </div>

                        {/* Content */}
                        {effectiveExGroup === '__nc__' ? (
                            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                                {nonCanonical.map(ex => (
                                    <button key={ex.pdbId} onClick={() => loadExample(ex.pdbId)} title={ex.note}
                                        style={{
                                            padding: '4px 12px', fontSize: 12, fontFamily: 'var(--mono)', fontWeight: 600,
                                            cursor: 'pointer', border: '1px solid var(--border-med)', borderRadius: 'var(--r-sm)',
                                            background: 'var(--surface)', color: 'var(--teal-dark)',
                                        }}>
                                        {ex.pdbId.toUpperCase()} · {ex.tetrads}T
                                    </button>
                                ))}
                            </div>
                        ) : (() => {
                            const grp = silvaData?.find(g => g.code === effectiveExGroup)
                            const subtypes = grp?.subtypes ?? []
                            const withExamples = subtypes.filter(s => (s.examples?.length ?? 0) > 0)
                            if (!grp) return <span style={{ color: 'var(--text-dim)', fontSize: 13 }}>Loading…</span>
                            if (withExamples.length === 0) return (
                                <span style={{ color: 'var(--text-dim)', fontSize: 13 }}>
                                    No deposited examples for group {effectiveExGroup}
                                </span>
                            )
                            return (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                                    {withExamples.map(sub => (
                                        <div key={sub.code}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 6 }}>
                                                <code style={{
                                                    fontSize: 12, fontWeight: 700, fontFamily: 'var(--mono)',
                                                    padding: '1px 6px', borderRadius: 'var(--r-sm)',
                                                    background: 'var(--surface2)', color: 'var(--teal-dark)',
                                                    border: '1px solid var(--border-med)',
                                                }}>{sub.code}</code>
                                                <code style={{ fontSize: 12, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>{sub.loop}</code>
                                                {sub.silva && <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>{sub.silva}</span>}
                                                {sub.onz && <span style={{
                                                    fontSize: 11, fontWeight: 600, fontFamily: 'var(--mono)',
                                                    color: sub.onz?.startsWith('O') ? 'var(--teal-dark)' : sub.onz?.startsWith('N') ? '#9333ea' : '#d97706',
                                                }}>{sub.onz}</span>}
                                                {sub.note && <span style={{ fontSize: 11, color: 'var(--text-dim)', fontStyle: 'italic' }}>{sub.note}</span>}
                                            </div>
                                            <div className={styles.examplesList}>
                                                {(sub.examples ?? []).map(ex => (
                                                    <button
                                                        key={ex.pdbId}
                                                        className={styles.exampleBtn}
                                                        onClick={() => loadExample(ex.pdbId, effectiveExGroup, sub.code)}
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
                                        </div>
                                    ))}
                                </div>
                            )
                        })()}

                        {silvaError && (
                            <span style={{ color: 'var(--err-text)', fontSize: 13 }}>Failed to load examples</span>
                        )}
                    </div>
                )}
            </div>

            {/* ── Mode toggle: Canonical / Non-canonical ── */}
            <div style={{ display: 'flex', gap: 0, marginBottom: 16, borderRadius: 'var(--r-md)', overflow: 'hidden', border: '1px solid var(--border-med)', alignSelf: 'flex-start', width: 'fit-content' }}>
                {[
                    { id: 'canonical', label: 'Canonical', desc: 'Auto-derive parameters from Silva classification' },
                    { id: 'noncanonical', label: 'Non-canonical', desc: 'Manually specify all structural parameters' },
                ].map(({ id, label, desc }) => (
                    <button
                        key={id}
                        onClick={() => {
                            setMode(id)
                            if (id === 'noncanonical') setExFilterGroup('__nc__')
                            else if (exFilterGroup === '__nc__') setExFilterGroup(silvaGroup)
                        }}
                        title={desc}
                        style={{
                            padding: '8px 20px',
                            fontSize: 13,
                            fontWeight: 600,
                            fontFamily: 'var(--sans)',
                            border: 'none',
                            cursor: 'pointer',
                            background: mode === id ? 'var(--teal)' : 'var(--surface)',
                            color: mode === id ? 'white' : 'var(--text-dim)',
                            transition: 'background 0.15s, color 0.15s',
                        }}
                    >
                        {label}
                    </button>
                ))}
            </div>

            {/* ── Step 1: Sequence & Structure Input (canonical only) ── */}
            {mode === 'canonical' && (
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
                            onBlur={() => {
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
                        Line 2: Nucleotide sequence (UPPERCASE = RNA · lowercase = DNA)
                    </span>
                    <span className={styles.legendItem}>
                        <span className={styles.ldot} style={{ background: '#B45309' }} />
                        Line 3: 14L structure — dot-bracket + <code>^</code> (length = sequence length)
                    </span>

                </div>

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
                            {[...(silvaData ?? [])].sort((a, b) => (SILVA_DISPLAY_ORDER[a.code] ?? 99) - (SILVA_DISPLAY_ORDER[b.code] ?? 99)).map(g => (
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
                        { lbl: 'Tetrads', val: defaults.tetrads, active: true },
                        { lbl: 'Type', val: defaults.type, active: false },
                        { lbl: 'Polarity', val: defaults.polarity, active: true },
                        { lbl: 'Twist [°]', val: defaults.twist, active: true },
                        { lbl: 'Rise [Å]', val: defaults.rise, active: false },
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
                    {mode === 'canonical' && <span className={styles.badge}>4</span>}
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
                        {/* Name / Sequence / Structure — mirrored from Step 1 (shared state → bidirectional sync) */}
                        <div className={styles.row}>
                            <div className={styles.label}>
                                Name
                                <small>Structure name</small>
                            </div>
                            <input
                                type="text"
                                style={{ width: '100%', fontSize: 13 }}
                                value={nameVal}
                                onChange={e => setNameVal(e.target.value)}
                                placeholder="e.g. pz74_mp_G14L"
                                spellCheck={false}
                            />
                        </div>
                        <div className={styles.row}>
                            <div className={styles.label}>
                                Sequence
                                <small>UPPERCASE = RNA · lowercase = DNA</small>
                            </div>
                            <input
                                type="text"
                                style={{ width: '100%', fontFamily: 'var(--mono)', fontSize: 13 }}
                                value={seqVal}
                                onChange={e => setSeqVal(e.target.value)}
                                placeholder="e.g. agggttaggg"
                                spellCheck={false}
                            />
                        </div>
                        <div className={styles.row}>
                            <div className={styles.label}>
                                Structure
                                <small>dot-bracket + ^ markers</small>
                            </div>
                            <input
                                type="text"
                                style={{ width: '100%', fontFamily: 'var(--mono)', fontSize: 13 }}
                                value={structVal}
                                onChange={e => setStructVal(e.target.value)}
                                placeholder="e.g. (((^^.^^.)))..."
                                spellCheck={false}
                            />
                        </div>

                        {/* Chi — glycosidic bond conformation */}
                        <div className={styles.row}>
                            <div className={styles.label}>
                                Chi (χ)
                                <small>S=syn · A=anti · .=default · length must match sequence</small>
                            </div>
                            <div style={{ width: '100%' }}>
                                <input type="text"
                                    style={{ width: '100%', fontFamily: 'var(--mono)', fontSize: 13 }}
                                    value={chiVal} onChange={e => setChiVal(e.target.value)}
                                    placeholder={`${seqVal.trim().length || 0} chars — e.g. S.....S.....SS.......SS.`}
                                    spellCheck={false} />
                                {chiVal && seqVal && chiVal.length !== seqVal.trim().length && (
                                    <div style={{ fontSize: 12, color: 'var(--warn-text)', marginTop: 4 }}>
                                        Chi length ({chiVal.length}) ≠ sequence length ({seqVal.trim().length})
                                    </div>
                                )}
                            </div>
                        </div>

                        {/* Sugar pucker — per residue like Chi, length must match sequence */}
                        <div className={styles.row}>
                            <div className={styles.label}>
                                Sugar pucker
                                <small>N=North/RNA · S=South/DNA · .=default · length must match sequence</small>
                            </div>
                            <div style={{ width: '100%' }}>
                                <input type="text"
                                    style={{ width: '100%', fontFamily: 'var(--mono)', fontSize: 13 }}
                                    value={pucker}
                                    onChange={e => setPucker(e.target.value)}
                                    placeholder={`${seqVal.trim().length || 0} chars — auto-generated (UPPERCASE→N, lowercase→S)`}
                                    spellCheck={false}
                                />
                                {pucker && seqVal && pucker.length !== seqVal.trim().length && (
                                    <div style={{ fontSize: 12, color: 'var(--warn-text)', marginTop: 4 }}>
                                        Sugar pucker length ({pucker.length}) ≠ sequence length ({seqVal.trim().length})
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

                        {/* Tetrad Polarity — R/L toggle per tetrad, auto-updates Twist */}
                        <div className={styles.row}>
                            <div className={styles.label}>
                                Tetrad polarity
                                <small>R = parallel (+) · L = antiparallel (−) · auto-updates Twist</small>
                            </div>
                            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                                {(() => {
                                    const tetrads = Math.max(1, countTetrads(structVal.trim()) || 2)
                                    const rlStr = orientToRL(orientVal)
                                    const chars = rlStr.split('')
                                    while (chars.length < tetrads) chars.push('R')
                                    chars.length = tetrads
                                    return chars.map((ch, i) => (
                                        <div key={i} style={{ display: 'flex', flexDirection: 'column', gap: 3, alignItems: 'center' }}>
                                            <span style={{ fontSize: 10, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>T{i + 1}</span>
                                            <div style={{ display: 'flex', borderRadius: 'var(--r-sm)', overflow: 'hidden', border: '1px solid var(--border-med)' }}>
                                                {['R', 'L'].map(btn => (
                                                    <button key={btn}
                                                        onClick={() => {
                                                            const newOrient = updateOrientPolarity(orientVal, i, btn)
                                                            setOrientVal(newOrient)
                                                            setTwist(autoTwistFromOrient(newOrient))
                                                        }}
                                                        style={{
                                                            padding: '5px 10px', fontSize: 13, fontFamily: 'var(--mono)', fontWeight: 600,
                                                            border: 'none', cursor: 'pointer',
                                                            background: ch === btn ? 'var(--teal)' : 'var(--surface)',
                                                            color: ch === btn ? 'white' : 'var(--text-dim)',
                                                            transition: 'background 0.12s',
                                                        }}
                                                    >{btn}</button>
                                                ))}
                                            </div>
                                        </div>
                                    ))
                                })()}
                                <span style={{ fontSize: 12, color: 'var(--text-dim)', fontFamily: 'var(--mono)', marginLeft: 8 }}>
                                    = {orientToRL(orientVal)}
                                </span>
                            </div>
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
                                    const steps = Math.max(1, tetrads - 1)
                                    const parts = twist.split(';').map(s => s.trim())
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
                                                    next[i] = e.target.value.replace(/[^0-9.\-]/g, '')
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
                            <div className={styles.label}>
                                Helical rise
                                <small>axial translation per step (Å)</small>
                            </div>
                            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 12 }}>
                                {(() => {
                                    const tetrads = Math.max(1, countTetrads(structVal.trim()) || 1)
                                    const steps = Math.max(1, tetrads - 1)
                                    const parts = rise.split(';').map(s => s.trim())
                                    while (parts.length < steps) parts.push('3.4')
                                    if (parts.length > steps) parts.length = steps
                                    return parts.map((val, i) => (
                                        <div key={i} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                                            <span style={{ fontSize: 11, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>
                                                T{i + 1}→T{i + 2}
                                            </span>
                                            <input type="text" value={val}
                                                onChange={e => { const n = [...parts]; n[i] = e.target.value.replace(/[^0-9.\-]/g, ''); setRise(n.join(';')) }}
                                                placeholder="3.4" style={{ width: 72, fontFamily: 'var(--mono)', textAlign: 'center' }} />
                                        </div>
                                    ))
                                })()}
                                <span style={{ fontSize: 12, color: 'var(--text-dim)', marginBottom: 8 }}>Å</span>
                            </div>
                        </div>

                    </div>
                )}
            </div>

            {parseError && (
                <div ref={errorRef} style={{
                    display: 'flex', alignItems: 'flex-start', gap: 8,
                    margin: '0 0 12px', padding: '10px 14px',
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
            {/* Submit */}
            <div className={styles.submitArea} style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8 }}>
                {hasErrors && hasInput && (
                    <div style={{ fontSize: 12, color: 'var(--err-text)', display: 'flex', alignItems: 'center', gap: 6 }}>
                        <WarnIcon />
                        {currentErrors.length === 1
                            ? currentErrors[0]
                            : `${currentErrors.length} validation errors — fix before running`}
                    </div>
                )}
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
        <polygon points="2,1 13,7 2,13" />
    </svg>
)

const SpinIcon = () => (
    <svg width="14" height="14" viewBox="0 0 14 14" fill="none" style={{ animation: 'spin .8s linear infinite' }}>
        <circle cx="7" cy="7" r="5" stroke="currentColor" strokeWidth="1.5" strokeDasharray="20" strokeDashoffset="5" />
        <style>{'@keyframes spin{to{transform:rotate(360deg)}}'}</style>
    </svg>
)

const WarnIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" style={{ flexShrink: 0 }}>
        <path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" />
        <line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" />
    </svg>
)