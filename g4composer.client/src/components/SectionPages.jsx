// ─── RetrieveSection ───────────────────────────────────────────────────────
import styles from './SimpleSection.module.css'

export function RetrieveSection() {
  return (
    <div>
      <h2 className={styles.heading}>Retrieve a previous job</h2>
      <p className={styles.sub}>
        Enter a job identifier to download a previously generated model.
        Results older than 30 days are automatically deleted from the server.
      </p>
      <div className={styles.card} style={{ maxWidth: 580, margin: '0 auto' }}>
        <div className={styles.cardTitle}>Job / Molecule ID</div>
        <div className={styles.retrieveRow}>
          <input type="text" placeholder="e.g. a3f2-91cc-4d8e-b712" />
          <button className={styles.retrieveBtn}>Retrieve</button>
        </div>
      </div>
    </div>
  )
}

// ─── DocsSection ───────────────────────────────────────────────────────────
export function DocsSection() {
  return (
    <div>
      <h2 className={styles.heading}>Documentation</h2>

      {/* Input format */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>Input Format</div>
        <p style={{ fontSize: 14, color: 'var(--text-sub)', lineHeight: 1.8, marginBottom: 16 }}>
          G4Composer accepts a three-field input: structure name, nucleotide sequence, and strand/structure annotation.
          Lowercase sequences are treated as DNA (C2′-endo, South sugar pucker), uppercase as RNA (C3′-endo, North).
        </p>
        <div className={styles.docsGrid}>
          <div>
            <p className={styles.docsSubTitle}>Strand structure notation</p>
            <table className={styles.docsTable}>
              <thead><tr><th>Symbol</th><th>Meaning</th></tr></thead>
              <tbody>
                <tr><td><code className={styles.tag}>A B C D</code></td><td>Strand label for tetrad-forming residue</td></tr>
                <tr><td><code className={styles.tag}>.</code></td><td>Loop / unpaired residue</td></tr>
                <tr><td><code className={styles.tag}>( )</code></td><td>Watson–Crick base pair</td></tr>
                <tr><td><code className={styles.tag}>[ ]</code></td><td>Pseudoknot pair</td></tr>
                <tr><td><code className={styles.tag}>^</code></td><td>Bulge residue</td></tr>
              </tbody>
            </table>
          </div>
          <div>
            <p className={styles.docsSubTitle}>Polarity → helical twist</p>
            <table className={styles.docsTable}>
              <thead><tr><th>Symbol</th><th>Twist</th><th>Topology</th></tr></thead>
              <tbody>
                <tr><td>&gt;&gt;</td><td>29°</td><td>Parallel</td></tr>
                <tr><td>&lt;&lt;</td><td>27°</td><td>Antiparallel</td></tr>
                <tr><td>&lt;&gt;</td><td>19°</td><td>Hybrid (chair/basket)</td></tr>
                <tr><td>&gt;&lt;</td><td>37°</td><td>Mixed / parallel DNA</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {/* Silva topology table */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>Silva G4 Topology Classification (canonical, 8 groups)</div>
        <p style={{ fontSize: 13, color: 'var(--text-dim)', marginBottom: 16, lineHeight: 1.7 }}>
          The canonical G-quadruplex topology classification follows the geometric formalism of
          Webba da Silva (2007) and its extensions (Karsisiotis 2013, Dvorkin 2018).
          Eight groups are defined by the Up/Down (U/D) orientation of the four G-runs.
          Only 14–15 of the 26 theoretical topologies have been observed experimentally.
        </p>
        <div style={{ overflowX: 'auto' }}>
          <table className={styles.docsTable} style={{ width: '100%', fontSize: 12 }}>
            <thead>
              <tr>
                <th>Group</th><th>G-columns</th><th>Name</th><th>Groove</th>
                <th>Subtypes (code)</th><th>Loop notation</th><th>ONZ</th><th>Exp. entries</th>
              </tr>
            </thead>
            <tbody>
              {[
                { g:'I',   cols:'UUDD', name:'antiparallel: basket2', groove:'mwmn',
                  sub:'3a 5a 8b 11b 12a',
                  loops:'-P-Lw-P / -PD+P / +Ln+P+Lw / +LnD-Lw / D-PD',
                  silva:'-(plp) / -pd+p / +(lpl) / +ld-l / d-pd', onz:'O N O N Z', n:18 },
                { g:'II',  cols:'UDUD', name:'antiparallel: chair', groove:'wnwn',
                  sub:'6a 6b', loops:'-Lw-Ln-Lw / +Ln+Lw+Ln', silva:'-(lll) / +(lll)', onz:'O O', n:65 },
                { g:'III', cols:'UDUU', name:'hybrid3', groove:'wnmm',
                  sub:'2b 7a 10b 13a',
                  loops:'+P+P+Ln / -Lw-Ln-P / +PD-Ln / -LwD+P',
                  silva:'+(ppl) / -(llp) / -pd-l / -ldp', onz:'O O N N', n:22 },
                { g:'IV',  cols:'UUUD', name:'hybrid2', groove:'mmwn',
                  sub:'2a 7b 10a 13b',
                  loops:'-P-P-Lw / +Ln+Lw+P / -PD+Lw / +LnD-P',
                  silva:'-(ppl) / +l-l-p / -pd+l / +ld-p', onz:'O O N N', n:17 },
                { g:'V',   cols:'UUDU', name:'hybrid1', groove:'mwnm',
                  sub:'9a 9b', loops:'-P-Lw-Ln / +P+Ln+Lw', silva:'-(pll) / +(pll)', onz:'O O', n:20 },
                { g:'VI',  cols:'UDDU', name:'antiparallel: basket', groove:'wmnm',
                  sub:'3b 5b 8a 11a 12b',
                  loops:'+P+Ln+P / +PD-P / -Lw-P-Ln / -LwD+Ln / D+PD',
                  silva:'+(plp) / +pd-p / -(lpl) / -ld+l / dpd', onz:'O N O N Z', n:26 },
                { g:'VII', cols:'UDDD', name:'hybrid4', groove:'wmmn',
                  sub:'4a 4b', loops:'-Lw-P-P / +Ln+P+P', silva:'-(lpp) / +(lpp)', onz:'O O', n:5 },
                { g:'VIII',cols:'UUUU', name:'parallel', groove:'mmmm',
                  sub:'1a 1b', loops:'-P-P-P / +P+P+P', silva:'-(ppp) / +(ppp)*', onz:'O O', n:188 },
              ].map(row => (
                <tr key={row.g}>
                  <td style={{ fontWeight: 700 }}>{row.g}</td>
                  <td><code style={{ fontFamily: 'var(--mono)', fontSize: 11 }}>{row.cols}</code></td>
                  <td>{row.name}</td>
                  <td><code style={{ fontFamily: 'var(--mono)', fontSize: 11 }}>{row.groove}</code></td>
                  <td style={{ fontFamily: 'var(--mono)', fontSize: 11 }}>{row.sub}</td>
                  <td style={{ fontFamily: 'var(--mono)', fontSize: 10, color: 'var(--text-dim)' }}>{row.loops}</td>
                  <td style={{ fontFamily: 'var(--mono)', fontSize: 11 }}>{row.onz}</td>
                  <td style={{ textAlign: 'right', fontWeight: 600 }}>{row.n}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p style={{ fontSize: 12, color: 'var(--text-dim)', marginTop: 12 }}>
          * left-handed only · # RNA only (with bulges) · Loop codes: P = propeller, L = lateral (w = wide, n = narrow), D = diagonal
        </p>
      </div>

      {/* Polarity defaults */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>Default Strand Polarity Labels</div>
        <p style={{ fontSize: 14, color: 'var(--text-sub)', lineHeight: 1.8, marginBottom: 12 }}>
          Strand polarity (R = right-handed, L = left-handed) is assigned automatically based on tetrad count,
          following experimental observations in the G4DB database:
        </p>
        <table className={styles.docsTable}>
          <thead><tr><th>Tetrad count</th><th>Default polarity</th><th>Basis</th></tr></thead>
          <tbody>
            <tr><td>2</td><td><code className={styles.tag}>RL</code></td><td>Most 2-tetrad antiparallel structures</td></tr>
            <tr><td>3</td><td><code className={styles.tag}>RLL</code></td><td>Most common in 3-tetrad hybrids and antiparallel</td></tr>
            <tr><td>4</td><td><code className={styles.tag}>RLRL</code></td><td>Alternating, consistent with 4-tetrad parallel</td></tr>
            <tr><td>5+</td><td><code className={styles.tag}>RLRL…</code></td><td>No experimental references — alternating proposed</td></tr>
          </tbody>
        </table>
      </div>

      {/* References */}
      <div className={styles.card}>
        <div className={styles.cardTitle}>References</div>
        <ol style={{ fontSize: 14, color: 'var(--text-sub)', lineHeight: 2, paddingLeft: 20 }}>
          <li>Webba da Silva M. <em>Chemistry</em> 2007;13(35):9738–45. doi: 10.1002/chem.200701255</li>
          <li>Karsisiotis AI, O'Kane C, Webba da Silva M. <em>Methods</em> 2013;64(1):28–35. doi: 10.1016/j.ymeth.2013.06.004</li>
          <li>Dvorkin SA, Karsisiotis AI, Webba da Silva M. <em>Sci Adv</em> 2018;4(8):eaat3007. doi: 10.1126/sciadv.aat3007</li>
        </ol>
      </div>
    </div>
  )
}

// ─── ContactSection ────────────────────────────────────────────────────────
export function ContactSection() {
  return (
    <div>
      <h2 className={styles.heading}>Contact</h2>
      <div className={styles.card} style={{ maxWidth: 620, margin: '0 auto' }}>
        <p style={{ fontSize: 15, color: 'var(--text-sub)', lineHeight: 1.85, marginBottom: 20 }}>
          For questions related to usage of the server, interpretation of results,
          or collaboration inquiries, please contact us via e-mail.
        </p>

        <a
          href="mailto:dariusz.aleksander.dziel@gmail.com"
          style={{ fontSize: 17, color: 'var(--teal)', fontWeight: 600, textDecoration: 'none', display: 'block', marginBottom: 28 }}
        >
          dariusz.aleksander.dziel@gmail.com
        </a>

        <div className={styles.cardTitle} style={{ marginBottom: 14 }}>Team</div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {[
            { name: 'Dariusz Dziel',       role: 'Server development, G4 modelling' },
            { name: 'Mariusz Popęda',      role: 'Computational biology' },
            { name: 'Joanna Szarzyńska',   role: 'G-quadruplex structural analysis' },
            { name: 'Tomasz Żok',          role: 'RNA/DNA structure informatics' },
            { name: 'Marta Szachniuk',     role: 'Principal investigator' },
          ].map(({ name, role }) => (
            <div key={name} style={{
              display: 'flex', justifyContent: 'space-between', alignItems: 'baseline',
              padding: '8px 0', borderBottom: '1px solid var(--border)',
            }}>
              <span style={{ fontWeight: 600, color: 'var(--text)', fontSize: 14 }}>{name}</span>
              <span style={{ color: 'var(--text-dim)', fontSize: 13 }}>{role}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

// ─── Footer ────────────────────────────────────────────────────────────────
export function Footer({ onNavigate }) {
  return (
    <footer className={styles.footer}>
      <p>
        <strong style={{ fontWeight: 600, color: 'var(--text-sub)' }}>G4Composer</strong>
        {' · '}G-Quadruplex 3D Modelling Server
        {' · '}
        <a href="/scalar/v1" target="_blank" rel="noreferrer" className={styles.footerLink}>API Docs ↗</a>
      </p>
      <p style={{ marginTop: 6 }}>
        Built with ASP.NET 10 + React · Docker / CYANA / Xplor-NIH
        {' · '}
        <button className={styles.footerBtn} onClick={() => onNavigate('contact')}>Contact</button>
      </p>
    </footer>
  )
}
