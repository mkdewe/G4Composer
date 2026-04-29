import styles from './SimpleSection.module.css'

const MOCK_ENTRIES = [
  { name: 'Mango-I',  status: 'ok',  msg: 'Valid' },
  { name: 'Spinach',  status: 'ok',  msg: 'Valid' },
  { name: 'Chili',    status: 'err', msg: 'Error: structure length mismatch' },
]

export default function BatchSection() {
  return (
    <div>
      <h2 className={styles.heading}>Batch Processing</h2>
      <p className={styles.sub}>
        Submit multiple sequences simultaneously. Supported aptamers: Mango-I/II/III, Spinach, Spinach2, Chili.
      </p>

      <div className={styles.card}>
        <div className={styles.cardTitle}><StepBadge>1</StepBadge>Upload or paste sequences</div>
        <div className={styles.dropZone}>
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" style={{ margin: '0 auto 12px', display: 'block', opacity: .4 }}>
            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/>
          </svg>
          <p>Drop a file here or <span className={styles.link}>browse from disk</span></p>
          <small>Accepted formats: .txt · .fasta · .fa · .g4</small>
        </div>
        <textarea rows={8} style={{ fontFamily: 'var(--mono)', fontSize: 14 }}
          placeholder={'>Mango-I\ngGGaGGaGGaGGa\n.(***.**.***..)'} spellCheck={false} />
      </div>

      <div className={styles.submitArea}>
        <button className={styles.btnRun}>Submit Batch</button>
      </div>

      <div className={styles.entryTable}>
        <div className={styles.entryHead}>
          <span style={{ width: 28 }}>#</span>
          <span style={{ flex: 1 }}>Name</span>
          <span>Status</span>
        </div>
        {MOCK_ENTRIES.map((e, i) => (
          <div key={e.name} className={styles.entryRow}>
            <span style={{ width: 28, color: 'var(--text-dim)', fontFamily: 'var(--mono)' }}>{i + 1}</span>
            <span style={{ flex: 1, fontFamily: 'var(--mono)' }}>{e.name}</span>
            <span className={e.status === 'ok' ? styles.badgeOk : styles.badgeErr}>{e.msg}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

function StepBadge({ children }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      width: 22, height: 22, borderRadius: '50%',
      background: 'var(--teal-light)', color: 'var(--teal-dark)',
      fontSize: 12, fontWeight: 600, flexShrink: 0,
    }}>{children}</span>
  )
}
