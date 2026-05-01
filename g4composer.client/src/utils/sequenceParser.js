/**
 * sequenceParser.js
 * Parses the multi-field textarea input and maps it to a QuadroInput-compatible object
 * for the quadro14L engine.
 *
 * Supported input formats:
 *
 *  A) Full .inp format (copy-paste from quadro14L .inp file):
 *      name      pz74_mp_G14L_70
 *      sequence  gcgatgcacc...
 *      structure ((((((...))))))
 *      chi       .................
 *      orient    A-;B-
 *      rise      3.4
 *      twist     29
 *      path      A1;B1;A4;B4;...
 *      test      n
 *      rm_level  0
 *      iteration 70
 *
 *  B) 3-line shorthand (name + sequence + structure):
 *      >StructureName
 *      ggttggtgtggttgg
 *      (((....)))^.^
 */

const TWIST_MAP    = { '>>': '29', '<<': '27', '<>': '19', '><': '37' }
const DEFAULT_TWIST = '29'

// ── Orient defaults per Silva topology group ──────────────────────────────────
// Parallel (UUUU) → all strands up; everything else defaults to antiparallel.
const ORIENT_BY_GROUP = {
  UUUU: 'A+;B+',
  UDUD: 'A+;B-',
  UDDU: 'A+;B-',
  UUDD: 'A+;B-',
  UDUU: 'A+;B-',
  UUUD: 'A+;B-',
  UUDU: 'A+;B-',
  UDDD: 'A+;B-',
}

export function orientFromGroup(group) {
  return ORIENT_BY_GROUP[group] ?? 'A+;B-'
}

// ── Polarity label by tetrad count (Webba da Silva notation) ──────────────────
export function defaultPolarityLabel(tetradCount) {
  if (tetradCount <= 1) return 'R'
  if (tetradCount === 2) return 'RL'
  if (tetradCount === 3) return 'RLL'
  if (tetradCount === 4) return 'RLRL'
  return Array.from({ length: tetradCount }, (_, i) => (i % 2 === 0 ? 'R' : 'L')).join('')
}

// ── Count tetrads from structure string ───────────────────────────────────────
// Counts G-tetrad-participating positions: anything that is NOT '.' or '(' or ')'.
// Works for both 14G (AB..BA) and 14L (^^^...^^^) notation. Total tetrad chars
// is always (tetrads × 4) because each tetrad has 4 G's, one per strand.
// Verified against every deposited example in StructureExamples.
export function countTetrads(structure) {
  if (!structure) return 0
  const tetradChars = (structure.match(/[^.()]/g) || []).length
  return Math.max(1, Math.round(tetradChars / 4))
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function detectSugarPucker(sequence) {
  // Uppercase letters indicate RNA / North pucker (C3'-endo)
  return /[A-Z]/.test(sequence) ? 'N' : 'S'
}

// ── Validation ────────────────────────────────────────────────────────────────

export function validateEntry(parsed) {
  if (!parsed.name?.trim())
    throw new Error('Missing structure name (line 1: >Name or field "name")')

  if (!parsed.sequence || parsed.sequence.length < 4)
    throw new Error('Sequence is too short (minimum 4 nucleotides)')

  if (!/^[ACGUTacgut]+$/.test(parsed.sequence))
    throw new Error('Sequence contains invalid characters (allowed: a c g u t — lowercase only)')

  if (!parsed.structure)
    throw new Error('Missing structure (line 3 or field "structure")')

}

// ── Full .inp key-value parser ────────────────────────────────────────────────

function parseInpFormat(raw) {
  const result = {}
  for (const line of raw.split('\n')) {
    const trimmed = line.trim()
    if (!trimmed) continue
    const spaceIdx = trimmed.search(/\s/)
    if (spaceIdx < 0) continue
    const key = trimmed.slice(0, spaceIdx).toLowerCase()
    const val = trimmed.slice(spaceIdx).trim()
    result[key] = val
  }

  if (!result.name || !result.sequence) return null

  const pathList = result.path
    ? result.path.split(';').map(s => s.trim()).filter(Boolean)
    : null

  const twist = result.twist?.trim() || DEFAULT_TWIST
  const rise  = parseFloat(result.rise)  || 3.4

  // "test n" in .inp → isTest: false (14L default is n / false)
  const testRaw = (result.test ?? result.istest ?? 'n').toLowerCase()
  const isTest  = testRaw === 'y'

  return {
    name:        result.name,
    sequence:    result.sequence.toLowerCase(),
    structure:   result.structure || '',
    chi:         result.chi || '',
    orient:      result.orient || 'A+;B-',
    rise,
    twist,
    path:        pathList,
    pathRaw:     result.path || '',
    isTest,
    RM_Level:    parseInt(result.rm_level ?? result.rmlevel ?? '0', 10),
    Iterations:  parseInt(result.iteration ?? result.iterations ?? '70', 10),
    sugarPucker: detectSugarPucker(result.sequence),
    twistKey:    '>>',
    _format:     'inp',
  }
}

// ── 3-line shorthand parser ───────────────────────────────────────────────────

export function parseInputLines(raw) {
  const trimmed = raw.trim()

  // Detect .inp key-value format by presence of "name " or "sequence " as first word
  if (/^(name|sequence)\s/im.test(trimmed)) {
    const result = parseInpFormat(trimmed)
    if (result) return result
  }

  // 3-line shorthand: >Name / sequence / structure
  const lines = trimmed
    .split('\n')
    .map(l => l.trim())
    .filter(l => l.length > 0)

  if (lines.length < 2)
    throw new Error('Minimum 2 lines required: >Name and Sequence (Structure optional)')

  const [nameLine, seqLine, structLine] = lines

  if (!nameLine.startsWith('>'))
    throw new Error('Line 1 must start with ">" (e.g. >143d_basket)')

  const name      = nameLine.slice(1).trim()
  const sequence  = seqLine.toLowerCase()
  const structure = structLine || ''

  const gFrac = (sequence.match(/g/g) || []).length / (sequence.length || 1)
  let twistKey = '>>'
  if (structure) {
    const opens  = (structure.match(/\(/g) || []).length
    const closes = (structure.match(/\)/g) || []).length
    if      (opens > closes)   twistKey = '><'
    else if (opens < closes)   twistKey = '<>'
    else if (gFrac < 0.35)     twistKey = '<<'
  }

  const twist       = TWIST_MAP[twistKey] ?? DEFAULT_TWIST
  const sugarPucker = detectSugarPucker(seqLine)
  const tetradCount = structure ? countTetrads(structure) : Math.floor(sequence.length / 8)

  return {
    name,
    sequence,
    structure,
    chi:         '',
    orient:      'A+;B-',
    rise:        3.4,
    twist,
    twistKey,
    sugarPucker,
    path:        null,
    pathRaw:     '',
    isTest:      false,
    RM_Level:    0,
    Iterations:  70,
    _format:     '3line',
    _tetradCount: tetradCount,
  }
}
