import { useState } from 'react'
import SequenceForm from './SequenceForm.jsx'
import MolstarViewer from './MolstarViewer.jsx'
import PdbPreview from './PdbPreview.jsx'
import RunLog from './RunLog.jsx'
import styles from './BuildSection.module.css'

export default function BuildSection({ pdbUrl, pdbBlob, runState, runStatus, jobInfo, onRun, onReset }) {
  const [activeTab, setActiveTab] = useState('viewer')

  function downloadPdb() {
    if (!pdbBlob) return
    const a = document.createElement('a')
    a.href = URL.createObjectURL(pdbBlob)
    a.download = `g4_${jobInfo?.jobId ?? 'result'}.pdb`
    a.click()
  }

  return (
    <div>
      <h2 className={styles.heading}>Build your G-quadruplex structure</h2>
      <p className={styles.sub}>
        Fill in the fields below to generate a 3D model. Parameters are computed automatically
        from the input; use the Advanced panel to override individual values.
      </p>

      {/* Form cards */}
      <SequenceForm onRun={onRun} runState={runState} jobInfo={jobInfo} />

      {/* Viewer panel — only shown once a run has started or completed */}
      {runState !== 'idle' && (
        <div className={styles.viewerPanel}>

          {/* Tab bar */}
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
              <button className={styles.actionBtn} onClick={onReset}>
                Reset
              </button>
              <button
                className={`${styles.actionBtn} ${styles.actionPrimary}`}
                disabled={!pdbBlob}
                onClick={downloadPdb}
              >
                ↓ Download .pdb
              </button>
            </div>
          </div>

          {/* Tab bodies */}
          <div className={styles.tabBody}>
            {activeTab === 'viewer' && (
              <MolstarViewer pdbUrl={pdbUrl} runState={runState} runStatus={runStatus} />
            )}
            {activeTab === 'pdb' && (
              <PdbPreview pdbBlob={pdbBlob} jobInfo={jobInfo} />
            )}
            {activeTab === 'log' && (
              <RunLog runState={runState} jobInfo={jobInfo} runStatus={runStatus} />
            )}
          </div>

          {/* Status bar */}
          <div className={styles.statusBar}>
            <div className={styles.statusLeft}>
              <span className={`${styles.dot} ${
                runState === 'running' ? styles.dotPulse :
                runState === 'done'    ? styles.dotGreen :
                runState === 'error'   ? styles.dotRed   : styles.dotGrey
              }`} />
              <span className={styles.statusText}>{runStatus}</span>
            </div>
            <div className={styles.statusRight}>
              {jobInfo && (
                <>
                  <StatusItem label="Job"   value={jobInfo.jobId} mono />
                  <StatusItem label="Atoms" value={jobInfo.atoms} />
                  {jobInfo.elapsed && (
                    <StatusItem label="Time" value={`${(+jobInfo.elapsed / 1000).toFixed(1)}s`} />
                  )}
                </>
              )}
              <StatusItem label="Backend" value="localhost:7112" />
            </div>
          </div>
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
