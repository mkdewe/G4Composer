export default function StatusBar({ status }) {
  const icons = {
    idle: null,
    loading: <SpinIcon />,
    success: <CheckIcon />,
    error: <ErrorIcon />,
  };

  const colorMap = {
    idle: "status--idle",
    loading: "status--loading",
    success: "status--success",
    error: "status--error",
  };

  if (status.type === "idle" && !status.message) return null;

  return (
    <div className={`status-bar ${colorMap[status.type] ?? ""}`}>
      <span className="status-icon">{icons[status.type]}</span>
      <span className="status-message">{status.message}</span>
      <style>{`
        .status-bar {
          display: flex; align-items: center; gap: .5rem;
          padding: .4rem 1rem; font-family: var(--font-mono); font-size: .75rem;
          border-top: 1px solid var(--border);
          flex-shrink: 0;
        }
        .status--idle    { color: var(--text-dim); }
        .status--loading { color: var(--accent); }
        .status--success { color: #4af09a; }
        .status--error   { color: var(--error); background: rgba(240,90,90,.06); }
        .status-icon { display: flex; align-items: center; flex-shrink: 0; }
        .status-icon svg { width: 13px; height: 13px; }
        .status-message { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      `}</style>
    </div>
  );
}

const SpinIcon = () => (
  <svg viewBox="0 0 16 16" fill="none" style={{ animation: "spin .8s linear infinite" }}>
    <circle cx="8" cy="8" r="5" stroke="currentColor" strokeWidth="1.5" strokeDasharray="20" strokeDashoffset="5"/>
    <style>{`@keyframes spin{to{transform:rotate(360deg)}}`}</style>
  </svg>
);
const CheckIcon = () => (
  <svg viewBox="0 0 16 16" fill="none">
    <circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="1.5"/>
    <polyline points="5,8.5 7,10.5 11,6" stroke="currentColor" strokeWidth="1.5" fill="none"/>
  </svg>
);
const ErrorIcon = () => (
  <svg viewBox="0 0 16 16" fill="none">
    <circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="1.5"/>
    <line x1="8" y1="5" x2="8" y2="9" stroke="currentColor" strokeWidth="1.5"/>
    <circle cx="8" cy="11.5" r=".6" fill="currentColor"/>
  </svg>
);
