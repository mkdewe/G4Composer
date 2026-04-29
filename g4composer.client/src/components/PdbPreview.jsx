import { useEffect, useState } from 'react'

export default function PdbPreview({ pdbBlob, jobInfo }) {
  const [text, setText] = useState(null)

  useEffect(() => {
    if (!pdbBlob) { setText(null); return }
    pdbBlob.text().then(t => setText(t)).catch(() => setText(null))
  }, [pdbBlob])

  if (!text) return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', flex: 1, color: 'var(--text-dim)', fontSize: 13, padding: '2rem' }}>
      No PDB data available.
    </div>
  )

  return (
    <pre style={{
      fontFamily: 'var(--mono)', fontSize: 12, lineHeight: 1.75,
      background: '#1a1d23', color: '#e2e8f0',
      padding: '1rem 1.25rem', overflow: 'auto', flex: 1, margin: 0,
    }}>
      {text.split('\n').map((line, i) => {
        const color = line.startsWith('ATOM') || line.startsWith('HETATM') ? '#9FE1CB'
                    : line.startsWith('REMARK')                            ? '#6B7280'
                    : line.startsWith('TER') || line.startsWith('END')     ? '#FCD34D'
                    : '#e2e8f0'
        return <span key={i} style={{ color }}>{line}{'\n'}</span>
      })}
    </pre>
  )
}
