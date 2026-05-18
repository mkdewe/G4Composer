/**
 * apiService.js
 * Communicates with the ASP.NET 10 backend at /api/Quadro11.
 * Vite proxy forwards all /api/* requests to https://localhost:7112.
 */

class ApiError extends Error {
  constructor(message, statusCode, details) {
    super(message)
    this.name = 'ApiError'
    this.statusCode = statusCode
    this.details = details
  }
}

async function parseErrorBody(response) {
  const ct = response.headers.get('Content-Type') ?? ''
  if (ct.includes('application/json')) {
    try {
      const json = await response.json()

      // ASP.NET Core ValidationProblemDetails: { title, errors: { field: [msgs] } }
      if (json?.errors && typeof json.errors === 'object' && !Array.isArray(json.errors)) {
        const fieldErrors = Object.entries(json.errors)
          .flatMap(([field, msgs]) => msgs.map(m => `${field}: ${m}`))
          .join('\n')
        return { message: json.title ?? 'Validation failed', details: fieldErrors }
      }

      // Our ValidationErrorDto: { message: "Validation failed.", details: ["err1", "err2"] }
      const rawDetails = json?.details ?? json?.Details ?? null
      const details = Array.isArray(rawDetails)
        ? rawDetails.join('\n')   // join list of validation errors into readable string
        : rawDetails

      // If details contains the actual errors, use them as the message for clarity
      const message = details
        ? `${json?.message ?? json?.Message ?? 'Error'}:\n${details}`
        : (json?.detail ?? json?.message ?? json?.Message ?? JSON.stringify(json))

      return { message, details }
    } catch { /* fall through */ }
  }
  const text = await response.text().catch(() => `HTTP ${response.status}`)
  return { message: text, details: null }
}

/**
 * Fetch Docker execution log for a completed job.
 * @param {string} jobId
 * @returns {Promise<string>} log text or empty string if not found
 */
export async function fetchDockerLog(jobId) {
  try {
    const res = await fetch(`/api/quadro11/log/${jobId}`)
    if (!res.ok) return ''
    return await res.text()
  } catch {
    return ''
  }
}

/**
 * Run Quadro11 computation.
 * @param {Array<object>} inputs        – Quadro11Input objects
 * @param {function}      onProgress    – callback(string) for status messages
 * @returns {Promise<{blob: Blob, headers: Headers}>}
 */
export async function runQuadro11(inputs, onProgress) {
  onProgress?.('Connecting to backend…')

  const controller = new AbortController()
  const timeoutId  = setTimeout(() => controller.abort(), 5 * 60 * 1000) // 5 min

  try {
    onProgress?.('Sending parameters to Quadro11 container…')

    const response = await fetch('/api/quadro11/run', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'chemical/x-pdb, application/json',
      },
      body: JSON.stringify(inputs),
      signal: controller.signal,
    })

    clearTimeout(timeoutId)

    if (!response.ok) {
      const { message: errorMsg, details: errorDetails } = await parseErrorBody(response)
      const messages = {
        400: `Invalid input: ${errorMsg}`,
        404: 'API endpoint not found — check Vite proxy configuration',
        422: `Validation error: ${errorMsg}`,
        500: `Server error (Docker/CYANA): ${errorMsg}`,
        503: 'Server unavailable — is the backend running?',
      }
      throw new ApiError(
        messages[response.status] ?? `HTTP ${response.status}: ${errorMsg}`,
        response.status,
        errorDetails
      )
    }

    onProgress?.('Receiving PDB file…')
    const blob = await response.blob()

    if (blob.size === 0) {
      throw new ApiError('Server returned an empty PDB file', 200, 'empty_response')
    }

    const finalBlob = blob.type === 'chemical/x-pdb'
      ? blob
      : new Blob([blob], { type: 'chemical/x-pdb' })

    // Read energy + dual-run headers
    const jobId     = response.headers.get('X-Job-Id')
    const stdEnergy = response.headers.get('X-Std-Energy')
    const altEnergy = response.headers.get('X-Alt-Energy')
    const hasAlt    = response.headers.get('X-Has-Alt') === '1'
    const winner    = response.headers.get('X-Winner') || 'standard'

    // Fetch alternative PDB if available
    let altBlob = null
    let altUrl  = null
    if (hasAlt && jobId) {
      try {
        const altRes = await fetch(`/api/quadro11/alt-pdb/${jobId}`)
        if (altRes.ok) {
          const raw = await altRes.blob()
          altBlob   = raw.type === 'chemical/x-pdb' ? raw : new Blob([raw], { type: 'chemical/x-pdb' })
          altUrl    = URL.createObjectURL(altBlob)
        }
      } catch { /* alt PDB is best-effort, don't fail the whole run */ }
    }

    // Fetch Docker execution log (best-effort, non-blocking)
    const dockerLog = jobId ? await fetchDockerLog(jobId) : ''

    onProgress?.('Loading structure into Mol*…')
    return {
      blob: finalBlob, headers: response.headers, dockerLog,
      stdEnergy: stdEnergy ? parseFloat(stdEnergy) : null,
      altEnergy:  altEnergy  ? parseFloat(altEnergy)  : null,
      hasAlt, winner, altBlob, altUrl,
    }

  } catch (err) {
    clearTimeout(timeoutId)

    if (err.name === 'AbortError') {
      throw new ApiError(
        'Request timed out (5 min) — Quadro11 container did not respond',
        408,
        'timeout'
      )
    }
    if (err instanceof ApiError) throw err

    throw new ApiError(
      `Network error: ${err.message}. Check that the backend (port 7112) is running and the Vite proxy is configured.`,
      0,
      'network_error'
    )
  }
}

/**
 * Check backend health.
 * @returns {Promise<{ok: boolean, status: string, dockerAvailable: boolean, imageExists: boolean}>}
 */
export async function checkHealth() {
  try {
    const response = await fetch('/api/quadro11/health', {
      signal: AbortSignal.timeout(5000),
    })
    if (!response.ok) return { ok: false, status: 'unreachable' }
    const data = await response.json()
    return { ok: true, ...data }
  } catch {
    return { ok: false, status: 'unreachable' }
  }
}

/**
 * Fetch example payload from the backend (used to pre-fill the form).
 * @returns {Promise<Array|null>}
 */
export async function fetchExample() {
  try {
    const response = await fetch('/api/quadro11/example')
    if (!response.ok) return null
    return await response.json()
  } catch {
    return null
  }
}

/**
 * Run the full RNA → G4 pipeline via SSE.
 * Yields parsed event objects as they arrive.
 * @param {string} sequence
 * @yields {object} SSE event objects
 */
export async function* runPipeline(sequence) {
  const response = await fetch('/api/pipeline/run', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sequence }),
  })

  if (!response.ok) throw new Error(`HTTP ${response.status}`)

  const reader  = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer    = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n')
    buffer = lines.pop() // keep the incomplete line
    for (const line of lines) {
      if (line.startsWith('data: ')) {
        try { yield JSON.parse(line.slice(6)) } catch { /* ignore malformed */ }
      }
    }
  }
}

/**
 * Fetch the standard PDB produced by the pipeline for a given job.
 * Returns an object URL, or null on failure.
 * @param {string} jobId
 * @returns {Promise<string|null>}
 */
export async function fetchPipelinePdb(jobId) {
  try {
    const res = await fetch(`/api/pipeline/pdb/${jobId}`)
    if (!res.ok) return null
    const blob = await res.blob()
    return URL.createObjectURL(new Blob([blob], { type: 'chemical/x-pdb' }))
  } catch {
    return null
  }
}

/**
 * Fetch all Silva groups with subtypes and example summaries.
 * Used to populate the classification picker and examples list.
 * @returns {Promise<Array|null>}
 */
export async function fetchNonCanonicalExamples() {
  try {
    const res = await fetch('/api/structures/noncanonical')
    if (!res.ok) return []
    return await res.json()
  } catch {
    return []
  }
}

export async function fetchSilvaGroups() {
  try {
    const response = await fetch('/api/structures/groups', {
      signal: AbortSignal.timeout(8000),
    })
    if (!response.ok) return null
    return await response.json()
  } catch {
    return null
  }
}

/**
 * Fetch full .inp data for a single example by PDB ID.
 * Called when user clicks "Load" on an example button.
 * @param {string} pdbId
 * @returns {Promise<object|null>}
 */
export async function fetchExampleDetail(pdbId) {
  try {
    const response = await fetch(`/api/structures/examples/${encodeURIComponent(pdbId)}`, {
      signal: AbortSignal.timeout(5000),
    })
    if (!response.ok) return null
    return await response.json()
  } catch {
    return null
  }
}
