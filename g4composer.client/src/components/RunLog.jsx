export default function RunLog({ runState, jobInfo, runStatus }) {
  const ts = new Date().toLocaleTimeString()

  const entries = [
    { t: ts, msg: 'POST /api/Quadro11/run',                   type: 'info' },
    { t: ts, msg: 'Creating job working directory',            type: 'default' },
    { t: ts, msg: 'Generating .inp configuration files',       type: 'default' },
    { t: ts, msg: 'Starting Docker: docker run --rm quadro11:latest', type: 'info' },
    { t: ts, msg: 'Container started, CYANA initialising',     type: 'default' },
    { t: ts, msg: 'Running energy minimisation — 1000 iterations', type: 'default' },
    { t: ts, msg: 'Convergence achieved',                       type: 'default' },
    { t: ts, msg: 'Writing result.pdb',                        type: 'default' },
    ...(runState === 'done' && jobInfo ? [
      { t: ts, msg: `✓ output.pdb — ${jobInfo.atoms} atoms`, type: 'ok' },
      { t: ts, msg: `✓ Job ${jobInfo.jobId} complete · Content-Type: chemical/x-pdb`, type: 'ok' },
    ] : []),
    ...(runState === 'error' ? [
      { t: ts, msg: `✗ Error: ${runStatus}`, type: 'err' },
    ] : []),
  ]

  const colorMap = { ok: '#9FE1CB', info: '#93C5FD', err: '#F87171', default: '#9CA3AF' }

  return (
    <pre style={{
      fontFamily: 'var(--mono)', fontSize: 11, lineHeight: 1.8,
      background: '#1a1d23', color: '#9CA3AF',
      padding: '1rem 1.25rem', overflow: 'auto', flex: 1, margin: 0,
    }}>
      {entries.map((e, i) => (
        <span key={i} style={{ color: colorMap[e.type] }}>
          <span style={{ color: '#4B5563', marginRight: 10 }}>[{e.t}]</span>
          {e.msg}{'\n'}
        </span>
      ))}
    </pre>
  )
}
