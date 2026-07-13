/**
 * batchParser.js
 *
 * Parses uploaded .inp / .txt files into an array of QuadroInput objects.
 *
 * Three block formats are accepted (auto-detected):
 *
 *   A) MINIMAL — exactly 5 non-empty lines, no key/value syntax:
 *        1. name           e.g. "143d"
 *        2. sequence       e.g. "agggttagggttagggttaggg"
 *        3. structure      e.g. "(((^^...^^...^^...^^)))"
 *        4. topology       e.g. "UDDU"  (must match a Silva group code)
 *        5. loop subtype   e.g. "11a"   (must belong to the topology)
 *
 *      Missing fields (path, orient, twist, chi, rise, iteration, rm_level, test)
 *      are auto-derived from topology + subtype + tetrad count.
 *
 *   B) FULL POSITIONAL — exactly 11 non-empty lines, no key/value syntax:
 *        1.  name           e.g. "3a"
 *        2.  sequence
 *        3.  structure
 *        4.  chi
 *        5.  orient
 *        6.  rise            (number)
 *        7.  twist           ('19' or '19;29' etc.)
 *        8.  path
 *        9.  test            ('y' or 'n')
 *        10. rm_level        (integer)
 *        11. iteration       (integer)
 *
 *   C) KEY/VALUE .inp — classic format, identified by lines starting with one of
 *      the known field names (name / sequence / structure / chi / orient /
 *      rise / twist / path / test / rm_level / iteration). Required keys:
 *      name, sequence, structure. Missing optional keys default to their B-format
 *      equivalents.
 *
 * Splitting rules:
 *   - Files containing key/value markers are treated as a SINGLE block — blank
 *     lines inside the file are ignored. This protects pz74-style .inp files.
 *   - Other files are split on blank lines so multi-FASTA-like .txt works.
 *
 * Per-block errors are collected; one bad block does not stop the rest.
 */

import { countTetrads } from './sequenceParser.js'
import { buildPath, buildDefaultTwist, scaleOrientToTetrads } from './silvaTopology.js'

const MINIMAL_LINES_COUNT = 5
const FULL_LINES_COUNT    = 11
// Standard CYANA iteration sweep used when an input carries no explicit iteration.
// Matches the backend QuadroInput default so "bez iteracji" batch inputs run the
// full 30→300 trajectory and expose which checkpoint had the best energy.
const DEFAULT_ITERATION_STEPS = [30, 50, 70, 100, 150, 300]
// UPPERCASE=RNA(A,C,G,U), lowercase=DNA(a,c,g,t), mixed allowed. Invalid: uppercase T, lowercase u.
// Quadro14L explicitly rejects lowercase 'u' (ERROR 2) — use uppercase 'U' for RNA uridine.
const ALLOWED_SEQ_CHARS = /^[ACGUacgt]+$/
const KNOWN_KEYS = new Set([
  'name', 'sequence', 'structure', 'chi', 'orient',
  'rise', 'twist', 'path', 'test', 'rm_level', 'iteration', 'iteration_steps',
])

/**
 * @typedef {Object} ParsedItem
 * @property {string} sourceFile
 * @property {number} blockIndex
 * @property {object} [input]
 * @property {string} [error]
 */

/**
 * Parse one uploaded file.
 * @param {File} file
 * @param {Array} silvaData  list of groups returned by /api/structures/groups
 * @returns {Promise<ParsedItem[]>}
 */
export async function parseFile(file, silvaData) {
  const text = await file.text()
  return parseText(text, file.name, silvaData)
}

/**
 * Split text into blocks and parse each one.
 */
export function parseText(text, sourceFile, silvaData) {
  const blocks = splitIntoBlocks(text ?? '')
  if (blocks.length === 0) {
    return [{ sourceFile, blockIndex: 0, error: 'File contains no usable blocks.' }]
  }

  return blocks.map((raw, blockIndex) => {
    try {
      const input = parseBlock(raw, silvaData)
      return { sourceFile, blockIndex, input }
    } catch (err) {
      return { sourceFile, blockIndex, error: err.message }
    }
  })
}

// ── Block splitting ──────────────────────────────────────────────────────
function splitIntoBlocks(text) {
  const trimmed = text.trim()
  if (!trimmed) return []

  // If the whole file uses key/value syntax (any line starts with a known key
  // followed by whitespace), treat it as a single block — blank lines are noise.
  if (looksLikeKeyValue(trimmed)) return [trimmed]

  // Otherwise split on blank lines (multi-FASTA-style .txt).
  return trimmed
    .split(/\n\s*\n+/)
    .map(b => b.trim())
    .filter(Boolean)
}

function looksLikeKeyValue(text) {
  // A file is "key/value" if at least one line starts with a known key followed
  // by whitespace and then a non-empty value.
  for (const line of text.split('\n')) {
    const m = line.match(/^([a-z_]+)\s+\S/i)
    if (m && KNOWN_KEYS.has(m[1].toLowerCase())) return true
  }
  return false
}

// ── Per-block parser ─────────────────────────────────────────────────────
function parseBlock(raw, silvaData) {
  // Detect format C first (key/value) — independent of line count.
  if (looksLikeKeyValue(raw)) return parseKeyValue(raw)

  const lines = raw.split('\n').map(l => l.trim()).filter(Boolean)
  if (lines.length === MINIMAL_LINES_COUNT) return parseMinimal(lines, silvaData)
  if (lines.length === FULL_LINES_COUNT)    return parseFull(lines)

  throw new Error(
    `Block has ${lines.length} non-empty line(s); expected exactly ${MINIMAL_LINES_COUNT} ` +
    `(minimal: name / sequence / structure / topology / subtype), ${FULL_LINES_COUNT} ` +
    `(full positional), or use key/value .inp format.`
  )
}

// ── FORMAT A — minimal (5 lines, auto-derive the rest) ───────────────────
function parseMinimal(lines, silvaData) {
  const [name, sequenceRaw, structure, topologyRaw, subtypeRaw] = lines
  const sequence = sequenceRaw.toLowerCase()
  const topology = topologyRaw.toUpperCase()
  const subtype  = subtypeRaw.toLowerCase()

  validateSequence(sequence)
  validateTopology(topology, subtype, topologyRaw, subtypeRaw, silvaData)

  const sub = silvaData
    .find(g => g.code === topology)
    .subtypes.find(s => s.code === subtype)

  const tetrads = Math.max(1, countTetrads(structure) || 1)
  let path = ''
  try { path = buildPath(sub.loop, tetrads) } catch { /* leave empty if unparseable */ }
  const orient = scaleOrientToTetrads('A+;B-', tetrads)
  const twist  = buildDefaultTwist(tetrads)

  return {
    name,
    sequence,
    structure,
    chi:         '',
    orient,
    rise:        '3.4',
    twist,
    path:        path ? path.split(';') : null,
    isTest:      false,
    rmLevel:     5,  // minimal format carries no rm_level → quadro/reference default 5
    // Minimal format carries no iteration value → run the standard 30→300 sweep so the
    // best-energy checkpoint is discovered (and the viewer exposes the full trajectory).
    iterationSteps: [...DEFAULT_ITERATION_STEPS],
    sugarPucker: /[U]/.test(sequenceRaw) ? 'N' : 'S',  // U (uppercase) = RNA = N pucker
  }
}

// ── FORMAT B — full positional (11 lines) ────────────────────────────────
function parseFull(lines) {
  // Position 9 (test) is forced to n. Position 10 (rm_level) is honoured (default 5);
  // it only governs work-file cleanup, not geometry. Line 11 (iteration) drives the step.
  const [name, sequenceRaw, structure, chi, orient,
         riseRaw, twistRaw, pathRaw, , rmLevelRaw, iterRaw] = lines
  const sequence = sequenceRaw.toLowerCase()
  validateSequence(sequence)

  // Rise can be multi-step e.g. "3.4;3.4" — keep as string, validate each part
  const riseStr = riseRaw.trim()
  for (const part of riseStr.split(';')) {
    const v = parseFloat(part.trim())
    if (!Number.isFinite(v) || v === 0)
      throw new Error(`Line 6 (rise): '${part.trim()}' is not a valid non-zero number.`)
  }

  return {
    name, sequence, structure, chi, orient, rise: riseStr,
    twist:       twistRaw,
    path:        pathRaw ? pathRaw.split(';').map(s => s.trim()).filter(Boolean) : null,
    isTest:      false,
    rmLevel:     parseRmLevel(rmLevelRaw),
    // Iteration steps come directly from the input file's line 11 — single ("100")
    // or comma-separated ("10,20,…,100"). Falls back to the standard 30→300 sweep.
    iterationSteps: parseIterationSteps(iterRaw),
    sugarPucker: /[U]/.test(sequenceRaw) ? 'N' : 'S',  // U (uppercase) = RNA = N pucker
  }
}

// ── FORMAT C — key/value .inp ────────────────────────────────────────────
function parseKeyValue(raw) {
  const fields = {}
  for (const line of raw.split('\n')) {
    const trimmed = line.trim()
    if (!trimmed) continue
    const spaceIdx = trimmed.search(/\s/)
    if (spaceIdx < 0) continue
    const key = trimmed.slice(0, spaceIdx).toLowerCase()
    const val = trimmed.slice(spaceIdx).trim()
    if (KNOWN_KEYS.has(key)) fields[key] = val
  }

  if (!fields.name)      throw new Error(`Missing required field 'name'.`)
  if (!fields.sequence)  throw new Error(`Missing required field 'sequence'.`)
  if (!fields.structure) throw new Error(`Missing required field 'structure'.`)

  // Key/value .inp files always have lowercase sequences (quadro14L format).
  // Accept lowercase u and any valid nucleotide — don't apply UI case convention.
  const sequence = fields.sequence.trim()
  validateSequenceInp(sequence)

  // Rise can be multi-step e.g. "3.4;3.4" — keep as string, validate each part
  const riseStr = fields.rise?.trim() ?? '3.4'
  for (const part of riseStr.split(';')) {
    const v = parseFloat(part.trim())
    if (!Number.isFinite(v) || v === 0)
      throw new Error(`Field 'rise': '${part.trim()}' is not a valid non-zero number.`)
  }

  return {
    name:        fields.name,
    sequence,
    structure:   fields.structure,
    chi:         fields.chi ?? '',
    orient:      fields.orient ?? 'A+;B-',
    rise:        riseStr,
    twist:       fields.twist ?? '29',
    path:        fields.path
                   ? fields.path.split(';').map(s => s.trim()).filter(Boolean)
                   : null,
    isTest:      false,
    rmLevel:     parseRmLevel(fields.rm_level),
    // Iteration steps from the .inp: prefer the comma-separated 'iteration_steps' field
    // ("100" or "10,20,…,100"), then the legacy single 'iteration'. Batch honours both
    // single- and multi-iteration files; an .inp with neither field falls back to the
    // standard 30→300 sweep so the best checkpoint is found automatically.
    iterationSteps: parseIterationSteps(fields.iteration_steps ?? fields.iteration),
    sugarPucker: /[U]/.test(fields.sequence) ? 'N' : 'S',  // U (uppercase) = RNA = N pucker
  }
}

// ── Shared helpers ─────────────────────────────────────────────────────────
// Parse a CYANA iteration spec into a list of positive integers. Accepts a single
// value ("100" → [100]) or a comma-separated list ("10,20,…,100" → [10,…,100]),
// so batch mode honours both single-iteration and multi-iteration .inp files.
// Returns the fallback (the standard 30→300 sweep) when nothing usable is given —
// so an .inp without an iteration field runs the full trajectory, not a lone step.
function parseIterationSteps(raw, fallback = DEFAULT_ITERATION_STEPS) {
  const steps = String(raw ?? '')
    .split(',')
    .map(s => parseInt(s.trim(), 10))
    .filter(n => Number.isFinite(n) && n > 0)
  return steps.length > 0 ? steps : fallback
}

// rm_level governs cleanup of quadro14L work files only (no geometry effect).
// Default to 5 (quadro/reference default) when the file omits or malforms it.
function parseRmLevel(raw, fallback = 5) {
  const v = parseInt(String(raw ?? '').trim(), 10)
  return Number.isFinite(v) && v >= 0 ? v : fallback
}

// ── Shared validators ────────────────────────────────────────────────────
function validateSequence(sequence) {
  if (!sequence) throw new Error('Sequence is empty.')
  if (/T/.test(sequence))
    throw new Error("Sequence contains uppercase 'T' — RNA residues use 'U' (uppercase).")
  if (/u/.test(sequence))
    throw new Error("Sequence contains lowercase 'u' — use uppercase 'U' for RNA uridine (quadro14L rejects lowercase 'u').")
  if (!ALLOWED_SEQ_CHARS.test(sequence))
    throw new Error('Sequence contains invalid characters — allowed: A C G U (RNA uppercase) · a c g t (DNA lowercase) · mixed sequences are OK.')
}

// Validation for .inp key/value files — same rules as UI: uppercase U for RNA, no lowercase u.
function validateSequenceInp(sequence) {
  if (!sequence) throw new Error('Sequence is empty.')
  if (/u/.test(sequence))
    throw new Error("Sequence contains lowercase 'u' — use uppercase 'U' for RNA uridine (quadro14L rejects lowercase 'u').")
  if (!/^[ACGUacgt]+$/.test(sequence))
    throw new Error('Sequence contains invalid characters — allowed: a c g t (DNA lowercase) or A C G U (RNA uppercase).')
}

function validateTopology(topology, subtype, topologyRaw, subtypeRaw, silvaData) {
  if (!silvaData || silvaData.length === 0) {
    throw new Error('Classification data not loaded yet — try again in a moment.')
  }
  const group = silvaData.find(g => g.code === topology)
  if (!group) {
    const codes = silvaData.map(g => g.code).join(', ')
    throw new Error(`Unknown topology '${topologyRaw}'. Allowed: ${codes}.`)
  }
  const sub = group.subtypes.find(s => s.code === subtype)
  if (!sub) {
    const codes = group.subtypes.map(s => s.code).join(', ')
    throw new Error(`Subtype '${subtypeRaw}' is not part of group ${topology}. Allowed: ${codes}.`)
  }
}
