/**
 * sequenceParser.js
 * Parses the multi-field textarea input and maps it to a QuadroInput-compatible object
 * for the quadro14L engine.
 *
 * Input format — either:
 *  A) 3-line shorthand:
 *      >StructureName
 *      ggttggtgtggttgg          ← sequence (lowercase recommended)
 *      AB..BA...AB..BA          ← structure (dot-bracket / strand labels)
 *
 *  B) Full .inp format (as used by quadro14G/14L):
 *      name     pz74_mp_G14L_70
 *      sequence gcgatgcacc...
 *      structure ((((((...)))))))
 *      chi      .................
 *      orient   A-;B-
 *      rise     3.4
 *      twist    29
 *      path     A1;B1;A4;B4;...
 *      test     n
 *      rm_level 5
 *      iteration 70
 */

const TWIST_MAP = { '>>': 29, '<<': 27, '<>': 19, '><': 37 }
const DEFAULT_TWIST = 29

// ── Default polarity labels by tetrad count ───────────────────────────────────
export function defaultPolarityLabel(tetradCount) {
  if (tetradCount <= 1) return 'R'
  if (tetradCount === 2) return 'RL'
  if (tetradCount === 3) return 'RLL'
  if (tetradCount === 4) return 'RLRL'
  // 5+: alternate starting with R, ending L
  return Array.from({ length: tetradCount }, (_, i) => i % 2 === 0 ? 'R' : 'L').join('')
}

// ── Count tetrads from structure string ───────────────────────────────────────
export function countTetrads(structure) {
  // Count distinct tetrad-forming positions (capital A/B letters or * markers)
  const matches = structure.match(/[A-Z]/g) || []
  // Each tetrad has 4 strands; approximate by dividing unique strand positions
  // More robustly: count positions where strand label changes
  const labels = new Set(matches)
  // Each letter like A1,B1,A2,B2 → number of unique numeric suffixes = tetrad count
  // For dot-bracket with *, count groups of consecutive *
  const stars = structure.match(/\*+/g)
  if (stars) return stars.length
  // For strand label notation like AB..BA: count distinct digit suffixes in path
  return Math.max(1, Math.floor(matches.length / 2))
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function detectSugarPucker(sequence) {
  return /[A-Z]/.test(sequence) ? 'N' : 'S'
}

function extractPathFromStructure(structure) {
  const path = []
  for (let i = 0; i < structure.length; i++) {
    if (structure[i] === '*') path.push(i + 1)
  }
  return path.length > 0 ? path : null
}

// ── Validation ────────────────────────────────────────────────────────────────

export function validateEntry(parsed) {
  if (!parsed.name?.trim())
    throw new Error('Missing structure name (line 1: >Name or field "name")')
  if (!parsed.sequence || parsed.sequence.length < 4)
    throw new Error('Sequence is too short (minimum 4 nucleotides)')
  if (!/^[ACGUTacgut]+$/.test(parsed.sequence))
    throw new Error('Sequence contains invalid characters (allowed: a c g u t / A C G U T)')
  if (!parsed.structure)
    throw new Error('Missing structure (line 3 or field "structure")')
}

// ── Full .inp key-value parser ────────────────────────────────────────────────

function parseInpFormat(raw) {
  const result = {}
  for (const line of raw.split('\n')) {
    const trimmed = line.trim()
    if (!trimmed) continue
    // split on first whitespace
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

  const twist = parseFloat(result.twist) || DEFAULT_TWIST
  const rise  = parseFloat(result.rise)  || 3.4

  return {
    name:        result.name,
    sequence:    result.sequence.toLowerCase(),
    structure:   result.structure || '',
    chi:         result.chi || '',
    orient:      result.orient || 'A+;B+',
    rise,
    twist,
    path:        pathList,
    isTest:      (result.test || result.istest || 'y') === 'y',
    RM_Level:    parseInt(result.rm_level || result.rmlevel || '5'),
    Iterations:  parseInt(result.iteration || result.iterations || '100'),
    sugarPucker: detectSugarPucker(result.sequence),
    twistKey:    '>>',
    _format:     'inp',
  }
}

// ── 3-line shorthand parser ───────────────────────────────────────────────────

export function parseInputLines(raw) {
  const trimmed = raw.trim()

  // Detect .inp key-value format (has "name" or "sequence" as first word on a line)
  if (/^(name|sequence)\s/im.test(trimmed)) {
    const result = parseInpFormat(trimmed)
    if (result) return result
  }

  // 3-line shorthand
  const lines = trimmed
    .split('\n')
    .map(l => l.trim())
    .filter(l => l.length > 0)

  if (lines.length < 2)
    throw new Error('Minimum 2 lines required: >Name and Sequence (Structure optional)')

  const [nameLine, seqLine, structLine] = lines

  if (!nameLine.startsWith('>'))
    throw new Error('Line 1 must start with ">" (e.g. >1hap_js12B)')

  const name      = nameLine.slice(1).trim()
  const sequence  = seqLine.toLowerCase()
  const structure = structLine || ''

  // Infer twist from sequence composition if no structure hints
  const gFrac = (sequence.match(/[g]/g) || []).length / (sequence.length || 1)
  let twistKey = '>>'
  if (structure) {
    const opens  = (structure.match(/\(/g) || []).length
    const closes = (structure.match(/\)/g) || []).length
    if      (opens > closes)   twistKey = '><'
    else if (opens < closes)   twistKey = '<>'
    else if (gFrac < 0.35)     twistKey = '<<'
  }

  const twist       = TWIST_MAP[twistKey] ?? DEFAULT_TWIST
  const sugarPucker = detectSugarPucker(seqLine) // use original case for pucker detection
  const path        = extractPathFromStructure(structure)
  const cleanStruct = structure.replace(/\*/g, '')

  return {
    name,
    sequence,
    structure:  cleanStruct,
    chi:        '',
    orient:     'A+;B+',
    rise:       3.4,
    twist,
    twistKey,
    sugarPucker,
    path,
    isTest:     true,
    RM_Level:   5,
    Iterations: 100,
    _format:    '3line',
  }
}
