import { useState } from 'react'

/**
 * RunLog — displays Docker execution log and job metadata in the "Run log" tab.
 *
 * Props:
 *   runState   - 'idle' | 'running' | 'done' | 'error'
 *   jobInfo    - { jobId, atoms, elapsed, name }
 *   runStatus  - current status string
 *   dockerLog  - raw Docker log text fetched from GET /api/quadro11/log/{jobId}
 */
export default function RunLog({ runState, jobInfo, runStatus, dockerLog }) {
  const [wrap, setWrap] = useState(false)

  const isRunning = runState === 'running'
  const isDone    = runState === 'done'
  const isError   = runState === 'error'

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 420 }}>

      {/* Job metadata bar */}
      {jobInfo && (
        <div style={{
          display: 'flex', gap: 24, padding: '10px 16px',
          background: 'var(--surface2)', borderBottom: '1px solid var(--border)',
          fontSize: 12, fontFamily: 'var(--mono)', flexWrap: 'wrap',
        }}>
          <MetaItem label="Job ID"    value={jobInfo.jobId} />
          <MetaItem label="Structure" value={jobInfo.name} />
          <MetaItem label="Atoms"     value={jobInfo.atoms} />
          {jobInfo.elapsed && (
            <MetaItem label="Elapsed" value={`${(+jobInfo.elapsed / 1000).toFixed(2)}s`} />
          )}
          <MetaItem
            label="Status"
            value={isDone ? 'Success' : isError ? 'Failed' : 'Running…'}
            color={isDone ? 'var(--ok-text)' : isError ? 'var(--err-text)' : 'var(--warn-text)'}
          />
        </div>
      )}

      {/* Log toolbar */}
      {dockerLog && (
        <div style={{
          display: 'flex', alignItems: 'center', gap: 10, justifyContent: 'flex-end',
          padding: '6px 12px', borderBottom: '1px solid var(--border)',
          background: 'var(--surface)',
        }}>
          <span style={{ fontSize: 11, color: 'var(--text-dim)', marginRight: 'auto' }}>
            Docker execution log
          </span>
          <button
            onClick={() => setWrap(v => !v)}
            style={{
              padding: '3px 10px', fontSize: 11, cursor: 'pointer',
              border: '1px solid var(--border-med)', borderRadius: 'var(--r-sm)',
              background: wrap ? 'var(--teal-light)' : 'var(--surface)',
              color: wrap ? 'var(--teal-dark)' : 'var(--text-dim)',
            }}
          >
            {wrap ? 'Unwrap' : 'Wrap'}
          </button>
          <button
            onClick={() => {
              const blob = new Blob([dockerLog], { type: 'text/plain' })
              const a = document.createElement('a')
              a.href = URL.createObjectURL(blob)
              a.download = `${jobInfo?.jobId ?? 'job'}_docker.log`
              a.click()
              setTimeout(() => URL.revokeObjectURL(a.href), 5000)
            }}
            style={{
              padding: '3px 10px', fontSize: 11, cursor: 'pointer',
              border: '1px solid var(--border-med)', borderRadius: 'var(--r-sm)',
              background: 'var(--surface)', color: 'var(--text-dim)',
            }}
          >
            ↓ .log
          </button>
        </div>
      )}

      {/* Log body */}
      <div style={{ flex: 1, overflow: 'auto', background: '#16181f' }}>
        {isRunning && !dockerLog && (
          <div style={{
            display: 'flex', alignItems: 'center', gap: 10,
            padding: '20px 16px', color: '#9FE1CB', fontSize: 13,
            fontFamily: 'var(--mono)',
          }}>
            <PulsingDot />
            {runStatus || 'Running…'}
          </div>
        )}

        {!isRunning && !dockerLog && (
          <div style={{
            padding: '20px 16px', color: '#555', fontSize: 13,
            fontFamily: 'var(--mono)',
          }}>
            {isError
              ? `Error: ${runStatus}`
              : 'No log available — start a computation to see Docker output here.'}
          </div>
        )}

        {dockerLog && (
          <pre style={{
            margin: 0, padding: '12px 16px',
            fontFamily: 'var(--mono)', fontSize: 11,
            color: '#c8d0e0', lineHeight: 1.7,
            whiteSpace: wrap ? 'pre-wrap' : 'pre',
            wordBreak: wrap ? 'break-all' : 'normal',
          }}>
            {dockerLog}
          </pre>
        )}
      </div>
    </div>
  )
}

function MetaItem({ label, value, color }) {
  return (
    <span>
      <span style={{ color: '#9ca3af', marginRight: 5 }}>{label}</span>
      <span style={{ color: color || '#e5e7eb', fontWeight: 500 }}>{value}</span>
    </span>
  )
}

function PulsingDot() {
  return (
    <span style={{
      display: 'inline-block', width: 8, height: 8, borderRadius: '50%',
      background: 'var(--teal)', flexShrink: 0,
      animation: 'pulse 1.2s ease-in-out infinite',
    }} />
  )
}
