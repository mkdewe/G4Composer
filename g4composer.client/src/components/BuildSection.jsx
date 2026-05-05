import { useState } from 'react'
import SequenceForm from './SequenceForm.jsx'
import MolstarViewer from './MolstarViewer.jsx'
import PdbPreview from './PdbPreview.jsx'
import RunLog from './RunLog.jsx'
import styles from './BuildSection.module.css'
import { downloadInp } from '../utils/inpSerializer.js'

export default function BuildSection({
  runs,
  activeRunId,
  activeRun,
  currentStatus,
  onRun,
  onReset,
  onSelectRun,
  onRemoveRun,
}) {
  const [activeTab, setActiveTab] = useState('viewer')

  const isRunning = activeRun?.state === 'running' ||
    runs.some(r => r.state === 'running')

  function downloadPdb() {
    if (!activeRun?.pdbBlob) return
    const a = document.createElement('a')
    a.href = URL.createObjectURL(activeRun.pdbBlob)
    a.download = `${sanitiseName(activeRun.name)}_g4.pdb`
    a.click()
  }

  function handleDownloadInp() {
    if (!activeRun?.inpContent) return
    downloadInp(activeRun.inpContent, activeRun.name)
  }

  return (
    <div>
      <h2 className={styles.heading}>Build your G-quadruplex structure</h2>
      <p className={styles.sub}>
        Fill in the fields below to generate a 3D model. Parameters are computed automatically
        from the input; use the Advanced panel to override individual values.
      </p>

      {/* Form */}
      <SequenceForm onRun={onRun} runState={activeRun?.state ?? 'idle'} />

      {/* Results panel — shown when there is at least one run */}
      {runs.length > 0 && (
        <div className={styles.viewerPanel}>

          {/* Run tabs (browser-tab style) */}
          <div className={styles.runTabBar}>
            <div className={styles.runTabs}>
              {runs.map(run => (
                <button
                  key={run.id}
                  className={`${styles.runTab} ${run.id === activeRunId ? styles.runTabActive : ''}`}
                  onClick={() => onSelectRun(run.id)}
                  title={run.name}
                >
                  {/* State indicator dot */}
                  <span className={`${styles.runDot} ${
                    run.state === 'running' ? styles.runDotPulse :
                    run.state === 'done'    ? styles.runDotGreen :
                    run.state === 'error'   ? styles.runDotRed   : styles.runDotGrey
                  }`} />
                  <span className={styles.runTabName}>{run.name}</span>
                  <button
                    className={styles.runTabClose}
                    onClick={e => { e.stopPropagation(); onRemoveRun(run.id) }}
                    title="Close"
                  >×</button>
                </button>
              ))}
            </div>
            <button className={styles.resetBtn} onClick={onReset} title="Close all runs">
              Clear all
            </button>
          </div>

          {/* Content tab bar */}
          {activeRun && (
            <>
              <div className={styles.tabBar}>
                <div className={styles.tabs}>
                  {[
                    { id: 'viewer', label: '3D Viewer (Mol*)' },
                    { id: 'pdb',    label: 'PDB source' },
                    { id: 'log',    label: 'Run log' },
                  ].map(({ id, label }) => (
                    <button
                      key={id}
                      className={`${styles.tab} ${activeTab === id ? styles.tabActive : ''}`}
                      onClick={() => setActiveTab(id)}
                    >
                      {label}
                    </button>
                  ))}
                </div>
                <div className={styles.tabActions}>
                  {activeRun.inpContent && (
                    <button
                      className={styles.actionBtn}
                      onClick={handleDownloadInp}
                      title="Download .inp input file"
                    >
                      ↓ .inp
                    </button>
                  )}
                  <button
                    className={`${styles.actionBtn} ${styles.actionPrimary}`}
                    disabled={!activeRun.pdbBlob}
                    onClick={downloadPdb}
                  >
                    ↓ .pdb
                  </button>
                </div>
              </div>

              {/* Tab content */}
              <div className={styles.tabBody}>
                {activeTab === 'viewer' && (
                  <MolstarViewer
                    pdbUrl={activeRun.pdbUrl}
                    runState={activeRun.state}
                    runStatus={activeRun.status}
                    structureName={activeRun.name}
                  />
                )}
                {activeTab === 'pdb' && (
                  <PdbPreview pdbBlob={activeRun.pdbBlob} jobInfo={activeRun.jobInfo} />
                )}
                {activeTab === 'log' && (
                  <RunLog
                    runState={activeRun.state}
                    jobInfo={activeRun.jobInfo}
                    runStatus={activeRun.status}
                    dockerLog={activeRun.dockerLog || ''}
                  />
                )}
              </div>

              {/* Status bar */}
              <div className={styles.statusBar}>
                <div className={styles.statusLeft}>
                  <span className={`${styles.dot} ${
                    activeRun.state === 'running' ? styles.dotPulse :
                    activeRun.state === 'done'    ? styles.dotGreen :
                    activeRun.state === 'error'   ? styles.dotRed   : styles.dotGrey
                  }`} />
                  <span className={styles.statusText}>{activeRun.status}</span>
                </div>
                <div className={styles.statusRight}>
                  {activeRun.jobInfo && (
                    <>
                      <StatusItem label="Job"   value={activeRun.jobInfo.jobId} mono />
                      <StatusItem label="Atoms" value={activeRun.jobInfo.atoms} />
                      {activeRun.jobInfo.elapsed && (
                        <StatusItem label="Time" value={`${(+activeRun.jobInfo.elapsed / 1000).toFixed(1)}s`} />
                      )}
                    </>
                  )}
                  <StatusItem label="Backend" value="localhost:7112" />
                </div>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  )
}

function StatusItem({ label, value, mono }) {
  return (
    <span style={{ fontFamily: 'var(--mono)', fontSize: 11, color: '#888' }}>
      <span style={{ color: '#bbb', marginRight: 4 }}>{label}</span>
      <span style={{ color: '#555', fontWeight: mono ? 600 : 400 }}>{value}</span>
    </span>
  )
}

function sanitiseName(name) {
  return (name || 'structure').replace(/[^a-z0-9._-]/gi, '_').slice(0, 80)
}
