/**
 * inpSerializer.js
 *
 * Generates the quadro14L .inp file content from a QuadroInput payload object.
 * Mirrors the backend QuadroEngineBase.SerializeInput logic so the downloaded
 * .inp can be fed directly back into quadro14L.exe.
 */

/**
 * Serialize a single QuadroInput payload to .inp text.
 * @param {object} input - QuadroInput-compatible object
 * @returns {string}
 */
export function serializeInp(input) {
  const name      = input.name      || 'structure'
  // Preserve original sequence case — backend IQuadroEngine.SerializeInput already
  // lowercases for quadro14L.exe. Keeping original case allows round-trip uploads.
  const sequence  = input.sequence || ''
  const structure = input.structure || ''
  const chi       = input.chi    || '.' .repeat(sequence.length)
  // Shugar: per-residue sugar pucker. Auto-generate from sequence case if empty.
  const shugar    = input.Shugar || input.shugar
    || sequence.split('').map(c => /[A-Z]/.test(c) ? 'N' : 'S').join('')
  const orient    = input.orient    || 'A+;B-'
  const rise      = String(input.rise  ?? '3.4').trim() || '3.4'
  const twist     = String(input.twist ?? '29').trim()  || '29'
  const path      = Array.isArray(input.path)
    ? input.path.join(';')
    : (input.path || '')
  const test      = 'n'
  // rm_level only affects cleanup of quadro14L work files, not geometry. Mirror the
  // backend/quadro default of 5; honour an explicit value if the payload carries one.
  const rmLevel   = input.rmLevel ?? input.RM_Level ?? 5
  const steps     = Array.isArray(input.iterationSteps) && input.iterationSteps.length > 0
    ? input.iterationSteps
    : [20, 40, 60, 80, 100]
  const iterationSteps = steps.join(',')

  // Match pz74 reference format — field name left-padded with spaces
  return [
    `name         ${name}`,
    `sequence    ${sequence}`,
    `structure    ${structure}`,
    `chi        ${chi}`,
    `shugar     ${shugar}`,
    `orient        ${orient}`,
    `rise                ${rise}`,
    `twist        ${twist}`,
    `path        ${path}`,
    `test               ${test}`,
    `rm_level           ${rmLevel}`,
    `iteration_steps    ${iterationSteps}`,
  ].join('\n') + '\n'
}

/**
 * Parse .inp text (key/value format produced by serializeInp / the pipeline)
 * back into a form-friendly object. Inverse of serializeInp.
 *
 * Used to hand a pipeline-generated structure over to the Build G4 form so the
 * user can edit the exact parameters and re-run. Tolerant: unknown lines are
 * ignored, missing keys fall back to sensible defaults.
 *
 * @param {string} text
 * @returns {{name,sequence,structure,chi,orient,rise,twist,path,Shugar,iterationSteps}}
 */
export function parseInp(text) {
  const fields = {}
  for (const line of String(text ?? '').split('\n')) {
    const trimmed = line.trim()
    if (!trimmed || trimmed.startsWith('#')) continue
    const spaceIdx = trimmed.search(/\s/)
    if (spaceIdx < 0) continue
    const key = trimmed.slice(0, spaceIdx).toLowerCase()
    const val = trimmed.slice(spaceIdx).trim()
    fields[key] = val
  }

  const sequence = fields.sequence ?? ''
  const steps = (fields.iteration_steps ?? fields.iteration ?? '')
    .split(',')
    .map(s => parseInt(s.trim(), 10))
    .filter(n => Number.isFinite(n) && n > 0)

  return {
    name:      fields.name ?? 'structure',
    sequence,
    structure: fields.structure ?? '',
    chi:       fields.chi ?? '',
    orient:    fields.orient ?? 'A+;B-',
    rise:      fields.rise ?? '3.4',
    twist:     fields.twist ?? '29',
    // keep path as a raw ';'-joined string for the form's text input
    path:      fields.path ?? '',
    Shugar:    fields.shugar ?? '',
    iterationSteps: steps.length > 0 ? steps : null,
  }
}

/**
 * Trigger a browser download of .inp content.
 * @param {string} inpContent - text from serializeInp()
 * @param {string} name - structure name used as filename
 */
export function downloadInp(inpContent, name) {
  const safe = (name || 'structure').replace(/[^a-z0-9._-]/gi, '_').slice(0, 80)
  const blob = new Blob([inpContent], { type: 'text/plain' })
  const a    = document.createElement('a')
  a.href     = URL.createObjectURL(blob)
  a.download = `${safe}.inp`
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 5000)
}
