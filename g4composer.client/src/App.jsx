import { useState, useRef, useCallback } from 'react'
import Nav from './components/Nav.jsx'
import Hero from './components/Hero.jsx'
import BuildSection from './components/BuildSection.jsx'
import BatchSection from './components/BatchSection.jsx'
import { RetrieveSection, DocsSection, ContactSection, Footer } from './components/SectionPages.jsx'
import { runQuadro11 } from './services/apiService.js'
import { serializeInp } from './utils/inpSerializer.js'

export default function App() {
  const [activeSection, setActiveSection] = useState('home')

  // ── Multi-run state ────────────────────────────────────────────────────────
  // Each run: { id, name, state, status, pdbBlob, pdbUrl, jobInfo, inpContent }
  const [runs,          setRuns]          = useState([])
  const [activeRunId,   setActiveRunId]   = useState(null)
  const [currentStatus, setCurrentStatus] = useState('')

  // Ref to the page content area — used for scrolling to top on section change
  const pageRef = useRef(null)

  // ── Navigation ────────────────────────────────────────────────────────────
  const navigate = useCallback((section) => {
    setActiveSection(section)
    requestAnimationFrame(() => {
      window.scrollTo({ top: 0, behavior: 'smooth' })
    })
  }, [])

  // ── Run handler ───────────────────────────────────────────────────────────
  const handleRun = useCallback(async (inputs) => {
    const runId   = crypto.randomUUID()
    const runName = inputs[0]?.name || 'Structure'

    // Generate .inp content from payload before sending (always have it)
    const inpContent = serializeInp(inputs[0])

    // Create new run entry in state — becomes the active tab immediately
    setRuns(prev => [...prev, {
      id:         runId,
      name:       runName,
      state:      'running',
      status:     'Starting Quadro container…',
      pdbBlob:    null,
      pdbUrl:     null,
      jobInfo:    null,
      inpContent,
    }])
    setActiveRunId(runId)
    setCurrentStatus('Starting Quadro container…')

    const updateRun = (patch) =>
      setRuns(prev => prev.map(r => r.id === runId ? { ...r, ...patch } : r))

    try {
      const { blob, headers, dockerLog } = await runQuadro11(inputs, (msg) => {
        setCurrentStatus(msg)
        updateRun({ status: msg })
      })

      const url    = URL.createObjectURL(blob)
      const jobId  = headers.get('X-Job-Id')     || '–'
      const atoms  = headers.get('X-Atom-Count') || '?'
      const elapsed = headers.get('X-Elapsed-Ms') || null

      updateRun({
        state:     'done',
        status:    `Model generated · ${atoms} atoms · Job ${jobId}`,
        pdbBlob:   blob,
        pdbUrl:    url,
        dockerLog: dockerLog || '',
        jobInfo: {
          jobId,
          atoms,
          elapsed,
          structures: inputs.length,
          name: runName,
        },
      })
      setCurrentStatus(`Model generated · ${atoms} atoms · Job ${jobId}`)
    } catch (err) {
      updateRun({
        state:  'error',
        status: err?.details || err?.message || 'Unknown server error',
      })
      setCurrentStatus(err?.details || err?.message || 'Unknown server error')
    }
  }, [])

  const handleRemoveRun = useCallback((runId) => {
    setRuns(prev => {
      const next = prev.filter(r => r.id !== runId)
      // Revoke URL to free memory
      const removed = prev.find(r => r.id === runId)
      if (removed?.pdbUrl) URL.revokeObjectURL(removed.pdbUrl)
      return next
    })
    setActiveRunId(prev => {
      if (prev !== runId) return prev
      // Switch to last remaining run or null
      const remaining = runs.filter(r => r.id !== runId)
      return remaining.length ? remaining[remaining.length - 1].id : null
    })
  }, [runs])

  const handleReset = useCallback(() => {
    runs.forEach(r => { if (r.pdbUrl) URL.revokeObjectURL(r.pdbUrl) })
    setRuns([])
    setActiveRunId(null)
    setCurrentStatus('')
  }, [runs])

  // ── Render ────────────────────────────────────────────────────────────────
  const showHome     = activeSection === 'home'
  const showBuild    = activeSection === 'home' || activeSection === 'build'
  const showBatch    = activeSection === 'batch'
  const showRetrieve = activeSection === 'retrieve'
  const showDocs     = activeSection === 'docs'
  const showContact  = activeSection === 'contact'

  const activeRun = runs.find(r => r.id === activeRunId) ?? null

  return (
    <>
      <Nav activeSection={activeSection} onNavigate={navigate} />
      {showHome && <Hero onNavigate={navigate} />}
      <div className="page" ref={pageRef}>
        {showBuild && (
          <BuildSection
            runs={runs}
            activeRunId={activeRunId}
            activeRun={activeRun}
            currentStatus={currentStatus}
            onRun={handleRun}
            onReset={handleReset}
            onSelectRun={setActiveRunId}
            onRemoveRun={handleRemoveRun}
          />
        )}
        {showBatch    && <BatchSection />}
        {showRetrieve && <RetrieveSection />}
        {showDocs     && <DocsSection />}
        {showContact  && <ContactSection />}
      </div>
      <Footer onNavigate={navigate} />
      <style>{`
        .page {
          max-width: 1060px;
          margin: 0 auto;
          padding: 0 24px 60px;
        }
      `}</style>
    </>
  )
}
