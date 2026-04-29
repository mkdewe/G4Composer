import styles from './Nav.module.css'

/* ── Icons (defined before LINKS to avoid TDZ errors) ── */

function G4Logo() {
  return (
    <svg width="34" height="34" viewBox="0 0 42 42" fill="none">
      <polygon points="21,7 33,14 21,21 9,14" fill="url(#n1)" opacity=".95"/>
      <polygon points="21,12 33,19 21,26 9,19" fill="url(#n2)" opacity=".9"/>
      <polygon points="21,17 33,24 21,31 9,24" fill="url(#n3)" opacity=".92"/>
      <circle cx="21" cy="24" r="3.5" fill="#C850C0" opacity=".85"/>
      <circle cx="21" cy="24" r="2"   fill="#FF69B4" opacity=".65"/>
      <polygon points="21,22 33,29 21,36 9,29" fill="url(#n4)" opacity=".85"/>
      <line x1="9"  y1="14" x2="9"  y2="29" stroke="#5DCAA5" strokeWidth=".7" opacity=".5"/>
      <line x1="33" y1="14" x2="33" y2="29" stroke="#5DCAA5" strokeWidth=".7" opacity=".5"/>
      <defs>
        <linearGradient id="n1" x1="9" y1="7"  x2="33" y2="21" gradientUnits="userSpaceOnUse"><stop stopColor="#2D9AC5"/><stop offset="1" stopColor="#5DCAA5"/></linearGradient>
        <linearGradient id="n2" x1="9" y1="12" x2="33" y2="26" gradientUnits="userSpaceOnUse"><stop stopColor="#1D7FA5"/><stop offset="1" stopColor="#3DBAA0"/></linearGradient>
        <linearGradient id="n3" x1="9" y1="17" x2="33" y2="31" gradientUnits="userSpaceOnUse"><stop stopColor="#185F95"/><stop offset="1" stopColor="#2D9A90"/></linearGradient>
        <linearGradient id="n4" x1="9" y1="22" x2="33" y2="36" gradientUnits="userSpaceOnUse"><stop stopColor="#0C3F75"/><stop offset="1" stopColor="#1D7575"/></linearGradient>
      </defs>
    </svg>
  )
}

function HomeIcon()   { return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg> }
function BuildIcon()  { return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="16"/><line x1="8" y1="12" x2="16" y2="12"/></svg> }
function BatchIcon()  { return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg> }
function SearchIcon() { return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg> }
function DocIcon()    { return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/></svg> }
function MailIcon()   { return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg> }

const LINKS = [
  { key: 'home',     label: 'G4Composer',    icon: <HomeIcon /> },
  { key: 'build',    label: 'Build G4',      icon: <BuildIcon /> },
  { key: 'batch',    label: 'Batch',         icon: <BatchIcon /> },
  { key: 'retrieve', label: 'Retrieve',      icon: <SearchIcon /> },
  { key: 'docs',     label: 'Documentation', icon: <DocIcon /> },
  { key: 'contact',  label: 'Contact',       icon: <MailIcon /> },
]

export default function Nav({ activeSection, onNavigate }) {
  return (
    <nav className={styles.nav}>
      <div className={styles.inner}>
        <button className={styles.logo} onClick={() => onNavigate('home')}>
          <G4Logo />
          <span className={styles.logoText}>
            <span className={styles.logoG4}>G4</span>
            <span className={styles.logoComposer}>COMPOSER</span>
          </span>
        </button>

        <div className={styles.links}>
          {LINKS.map(({ key, label, icon }) => (
            <button
              key={key}
              className={`${styles.link} ${activeSection === key ? styles.active : ''}`}
              onClick={() => onNavigate(key)}
            >
              {icon}
              {label}
            </button>
          ))}
        </div>

        <div className={styles.right}>
          <a
            className={styles.apiPill}
            href="/scalar/v1"
            target="_blank"
            rel="noreferrer"
            title="Open API documentation"
          >
            <span className={styles.apiDot} />
            API Online
          </a>
        </div>
      </div>
    </nav>
  )
}
