import { useState, useRef, useCallback, useEffect } from 'react'
import Nav from './components/Nav.jsx'
import Hero from './components/Hero.jsx'
import BuildSection from './components/BuildSection.jsx'
import BatchSection from './components/BatchSection.jsx'
import { RetrieveSection, DocsSection, ContactSection, Footer } from './components/SectionPages.jsx'
import { runQuadro11 } from './services/apiService.js'

export default function App() {
  const [activeSection, setActiveSection] = useState('home')

  // ── Run state ─────────────────────────────────────────────────────────────
  const [runState, setRunState]   = useState('idle')
  const [runStatus, setRunStatus] = useState('')
  const [pdbBlob, setPdbBlob]     = useState(null)
  const [pdbUrl, setPdbUrl]       = useState(null)
  const [jobInfo, setJobInfo]     = useState(null)
  const prevUrlRef                = useRef(null)

  // Ref to the page content area — used for scrolling to top on section change
  const pageRef = useRef(null)

  // ── Navigation ────────────────────────────────────────────────────────────
  const navigate = useCallback((section) => {
    setActiveSection(section)
    // Scroll the window to the top; use requestAnimationFrame so the DOM
    // has time to re-render before we measure scroll position.
    requestAnimationFrame(() => {
      window.scrollTo({ top: 0, behavior: 'smooth' })
    })
  }, [])

  // ── Run handler ───────────────────────────────────────────────────────────
  const handleRun = useCallback(async (inputs) => {
    setRunState('running')
    setRunStatus('Starting Quadro11 container…')
    setJobInfo(null)

    if (prevUrlRef.current) {
      URL.revokeObjectURL(prevUrlRef.current)
      prevUrlRef.current = null
    }
    setPdbBlob(null)
    setPdbUrl(null)

    try {
      const { blob, headers } = await runQuadro11(inputs, (msg) => setRunStatus(msg))

      const url = URL.createObjectURL(blob)
      prevUrlRef.current = url
      setPdbBlob(blob)
      setPdbUrl(url)

      const jobId   = headers.get('X-Job-Id')     || '–'
      const atoms   = headers.get('X-Atom-Count') || '?'
      const elapsed = headers.get('X-Elapsed-Ms') || null

      setJobInfo({ jobId, atoms, elapsed, structures: inputs.length, name: inputs[0]?.name || 'Structure' })
      setRunState('done')
      setRunStatus(`Model generated successfully · ${atoms} atoms · Job ${jobId}`)
    } catch (err) {
      setRunState('error')
      setRunStatus(err?.details || err?.message || 'Unknown server error')
    }
  }, [])

  const handleReset = useCallback(() => {
    if (prevUrlRef.current) {
      URL.revokeObjectURL(prevUrlRef.current)
      prevUrlRef.current = null
    }
    setPdbBlob(null)
    setPdbUrl(null)
    setRunState('idle')
    setRunStatus('')
    setJobInfo(null)
  }, [])

  // ── Render ────────────────────────────────────────────────────────────────
  const showHome     = activeSection === 'home'
  const showBuild    = activeSection === 'home' || activeSection === 'build'
  const showBatch    = activeSection === 'batch'
  const showRetrieve = activeSection === 'retrieve'
  const showDocs     = activeSection === 'docs'
  const showContact  = activeSection === 'contact'

  return (
    <>
      <Nav activeSection={activeSection} onNavigate={navigate} />

      {showHome && <Hero onNavigate={navigate} />}

      <div className="page" ref={pageRef}>
        {showBuild && (
          <BuildSection
            pdbUrl={pdbUrl}
            pdbBlob={pdbBlob}
            runState={runState}
            runStatus={runStatus}
            jobInfo={jobInfo}
            onRun={handleRun}
            onReset={handleReset}
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
