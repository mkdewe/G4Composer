import styles from './Hero.module.css'

export default function Hero({ onNavigate }) {
  return (
    <div className={styles.hero}>
      <div className={styles.logoWrap}>
        <svg width="60" height="60" viewBox="0 0 42 42" fill="none">
          <polygon points="21,5 34,12.5 34,27.5 21,35 8,27.5 8,12.5" stroke="#5DCAA5" strokeWidth=".5" fill="none" opacity=".35"/>
          <polygon points="21,7 33,14 21,21 9,14"  fill="url(#h1)" opacity=".95"/>
          <polygon points="21,12 33,19 21,26 9,19" fill="url(#h2)" opacity=".9"/>
          <polygon points="21,17 33,24 21,31 9,24" fill="url(#h3)" opacity=".92"/>
          <circle cx="21" cy="24" r="4"   fill="#C850C0" opacity=".9"/>
          <circle cx="21" cy="24" r="2.2" fill="#FF69B4" opacity=".75"/>
          <polygon points="21,22 33,29 21,36 9,29" fill="url(#h4)" opacity=".85"/>
          <line x1="9"  y1="14" x2="9"  y2="29" stroke="#5DCAA5" strokeWidth=".8" opacity=".5"/>
          <line x1="33" y1="14" x2="33" y2="29" stroke="#5DCAA5" strokeWidth=".8" opacity=".5"/>
          <defs>
            <linearGradient id="h1" x1="9" y1="7"  x2="33" y2="21" gradientUnits="userSpaceOnUse"><stop stopColor="#2D9AC5"/><stop offset="1" stopColor="#5DCAA5"/></linearGradient>
            <linearGradient id="h2" x1="9" y1="12" x2="33" y2="26" gradientUnits="userSpaceOnUse"><stop stopColor="#1D7FA5"/><stop offset="1" stopColor="#3DBAA0"/></linearGradient>
            <linearGradient id="h3" x1="9" y1="17" x2="33" y2="31" gradientUnits="userSpaceOnUse"><stop stopColor="#185F95"/><stop offset="1" stopColor="#2D9A90"/></linearGradient>
            <linearGradient id="h4" x1="9" y1="22" x2="33" y2="36" gradientUnits="userSpaceOnUse"><stop stopColor="#0C3F75"/><stop offset="1" stopColor="#1D7575"/></linearGradient>
          </defs>
        </svg>
        <span className={styles.heroLogoText}>
          <span className={styles.heroG4}>G4</span>
          <span className={styles.heroComposer}>COMPOSER</span>
        </span>
      </div>

      <p className={styles.heroLabel}>G-Quadruplex 3D Structure Prediction Server</p>
      <p className={styles.heroDesc}>
        G4Composer is a specialised web tool for modelling three-dimensional structures of G-quadruplex
        nucleic acids. The platform accepts sequence and dot-bracket structure annotations with the
        Silva (2020) loop-type classification to generate energy-minimised 3D models, serving as
        starting points for molecular docking, dynamic simulations, and other computational studies.
      </p>

      <div className={styles.heroCtas}>
        <button className={styles.ctaPrimary} onClick={() => onNavigate('build')}>
          Build a structure
        </button>
        <button className={styles.ctaSecondary} onClick={() => onNavigate('docs')}>
          Documentation
        </button>
      </div>
    </div>
  )
}
