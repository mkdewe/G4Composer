/**
 * silvaTopology.js
 *
 * Derives the quadro14L path string and a default orient from Silva loop notation
 * (e.g. "+Ln+P+Lw") and the number of tetrad planes.
 *
 * Mirror of backend Domain/SilvaTopology.cs — keep both in sync. Verified against
 * every example in StructureExamples that has a path field.
 *
 * Algorithm:
 *   * G-quadruplexes always have 4 strands (G-tracks numbered 1..4).
 *   * Number of tetrad planes equals N (1..4); each plane gets a letter A..D.
 *   * Walk starts at (track=1, direction=Forward). Per loop:
 *       - L (Lw/Ln) → ±1 track, flips direction
 *       - P         → ±1 track, keeps direction
 *       - D         → ±2 tracks, flips direction (unsigned 'D' = +D)
 *   * Track wraps mod 4 so that 1 - 1 → 4.
 *   * After 3 loops we have 4 stops; for each emit N letters with current track,
 *     reversed when direction is Reverse.
 */

const STRAND_COUNT = 4
const PLANE_LETTERS = ['A', 'B', 'C', 'D']
const LOOP_TOKEN_REGEX = /([+-]?)(Lw|Ln|L|P|D)/g

/**
 * Builds the tetrad path from Silva loop notation.
 * @param {string} loopNotation e.g. "+Ln+P+Lw"
 * @param {number} tetrads 1-4
 * @returns {string} e.g. "A1;B1;C1;C2;B2;A2;C3;B3;A3;A4;B4;C4"
 */
export function buildPath(loopNotation, tetrads) {
  if (!loopNotation) throw new Error('Loop notation is required')
  if (tetrads < 1 || tetrads > 4) throw new Error(`Tetrads must be 1-4, got ${tetrads}`)

  const loops = parseLoops(loopNotation)
  if (loops.length !== 3) {
    throw new Error(`Expected 3 loops in '${loopNotation}', got ${loops.length}`)
  }

  let track = 1
  let forward = true
  const visited = [{ track, forward }]

  for (const { sign, isDiagonal, flipsDirection } of loops) {
    const delta = sign * (isDiagonal ? 2 : 1)
    track = wrapTrack(track + delta)
    if (flipsDirection) forward = !forward
    visited.push({ track, forward })
  }

  const letters = PLANE_LETTERS.slice(0, tetrads)
  const parts = []
  for (const { track: t, forward: fwd } of visited) {
    const ordered = fwd ? letters : [...letters].reverse()
    for (const letter of ordered) parts.push(`${letter}${t}`)
  }
  return parts.join(';')
}

/**
 * Default orient string per tetrad count. Most common pattern observed
 * across deposited examples — user can override.
 */
export function buildDefaultOrient(tetrads) {
  switch (tetrads) {
    case 1: return 'A+'
    case 2: return 'A+;B-'
    case 3: return 'A+;B-;C-'
    case 4: return 'A+;B-;C+;D-'
    default: throw new Error(`Invalid tetrads: ${tetrads}`)
  }
}

function wrapTrack(t) {
  const zeroBased = ((t - 1) % STRAND_COUNT + STRAND_COUNT) % STRAND_COUNT
  return zeroBased + 1
}

function parseLoops(notation) {
  const loops = []
  for (const match of notation.matchAll(LOOP_TOKEN_REGEX)) {
    const sign = match[1] === '-' ? -1 : 1   // missing or '+' → +1
    const kind = match[2]
    const isDiagonal = kind === 'D'
    const flipsDirection = kind !== 'P'      // L and D flip, P keeps
    loops.push({ sign, isDiagonal, flipsDirection })
  }
  return loops
}


/**
 * Builds default multi-step twist for N tetrads.
 * Twist has (N-1) steps separated by ';' (one per inter-tetrad transition).
 * Defaults: '19' for inter-tetrad antiparallel, '29' for parallel terminal.
 * Heuristic that works for most deposited examples — user can override.
 */
export function buildDefaultTwist(tetrads) {
  if (tetrads <= 1) return '29'
  if (tetrads === 2) return '19'
  // For 3+ tetrads use alternating: '19;29' for 3T, '19;37;19' for 4T
  if (tetrads === 3) return '19;29'
  if (tetrads === 4) return '19;37;19'
  return Array(tetrads - 1).fill('29').join(';')
}

/**
 * Scales an orient string (e.g. "A+;B-") to N tetrads by adding/removing letters.
 * If the input already has N entries, returns it unchanged. If shorter, appends
 * '-' for additional planes (most common pattern). If longer, truncates.
 */
export function scaleOrientToTetrads(orient, tetrads) {
  const letters = ['A', 'B', 'C', 'D'].slice(0, tetrads)
  const parts = (orient || '').split(';').map(s => s.trim()).filter(Boolean)

  // If counts already match, return as-is.
  if (parts.length === tetrads) return parts.join(';')

  // Build a sign map keyed by letter from existing parts
  const signByLetter = {}
  for (const p of parts) {
    const m = p.match(/^([A-D])([+-])$/)
    if (m) signByLetter[m[1]] = m[2]
  }
  // Default sign for missing planes: '-' (matches the most common pattern)
  return letters.map(L => `${L}${signByLetter[L] ?? '-'}`).join(';')
}

/**
 * Default multi-step rise for N tetrads (N-1 values, all 3.4).
 */
export function buildDefaultRise(tetrads) {
  const steps = Math.max(1, tetrads - 1)
  return Array(steps).fill('3.4').join(';')
}

/**
 * Default multi-step sugar pucker for N tetrads.
 * @param {'S'|'N'} type - 'S' for DNA, 'N' for RNA
 */
export function buildDefaultPucker(tetrads, type = 'S') {
  const steps = Math.max(1, tetrads - 1)
  return Array(steps).fill(type).join(';')
}

/**
 * Auto-generates the chi (glycosidic bond) string from structure, path, and orient.
 *
 * Rule — for each G at a tetrad position:
 *   U-strand (tetrad letters ascending A→B→C in sequence order) + '+' sign → SYN ('S')
 *   D-strand (tetrad letters descending C→B→A in sequence order) + '−' sign → SYN ('S')
 *   Any other combination                                                    → ANTI ('.')
 *
 * Non-tetrad positions (loops, WC pairs) are always '.'.
 *
 * Works for standard ABCD and '^' structure notation. Returns '' if inputs are
 * inconsistent (path entry count ≠ tetrad position count).
 *
 * @param {string} structure  e.g. "ABC...CBA...ABC" or "^^^...^^^"
 * @param {string} path       e.g. "A1;B1;C1;C4;B4;A4;..."
 * @param {string} orient     e.g. "A+;B-;C-"
 * @returns {string}          chi string, same length as structure
 */
export function buildChiFromOrient(structure, path, orient) {
  if (!structure || !path || !orient) return ''

  // Parse orient → sign per tetrad letter
  const orientSign = {}
  for (const part of orient.split(';')) {
    const m = part.trim().match(/^([A-D])([+-])$/)
    if (m) orientSign[m[1]] = m[2]
  }

  // Parse path into ordered (tetrad, strand) entries
  const pathEntries = path.split(';').map(s => {
    const m = s.trim().match(/^([A-D])(\d)$/)
    return m ? { tetrad: m[1], strand: parseInt(m[2]) } : null
  }).filter(Boolean)

  if (!pathEntries.length) return ''

  // Collect tetrad positions from structure (A-D or ^)
  const tetradIndices = []
  for (let i = 0; i < structure.length; i++) {
    if (/[ABCD^]/.test(structure[i])) tetradIndices.push(i)
  }

  if (tetradIndices.length !== pathEntries.length) return ''

  // Pair each tetrad position with its path entry
  const gEntries = tetradIndices.map((pos, i) => ({
    seqPos: pos, tetrad: pathEntries[i].tetrad, strand: pathEntries[i].strand,
  }))

  // Determine direction of each strand: ascending tetrad letters → U, descending → D
  const byStrand = {}
  for (const e of gEntries) {
    if (!byStrand[e.strand]) byStrand[e.strand] = []
    byStrand[e.strand].push(e)
  }
  const strandDir = {}
  for (const [strand, entries] of Object.entries(byStrand)) {
    const letters = entries.map(e => e.tetrad)
    const ascending = letters.every((t, i) => i === 0 || t > letters[i - 1])
    strandDir[strand] = ascending ? 'U' : 'D'
  }

  // Build chi: '.' at all positions, 'S' where rule fires
  const chi = Array(structure.length).fill('.')
  for (const { seqPos, tetrad, strand } of gEntries) {
    const sign = orientSign[tetrad] ?? '+'
    const dir  = strandDir[strand]
    if ((dir === 'U' && sign === '+') || (dir === 'D' && sign === '-')) {
      chi[seqPos] = 'S'
    }
  }
  return chi.join('')
}
