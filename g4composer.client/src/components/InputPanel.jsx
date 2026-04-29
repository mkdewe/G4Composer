import { useState, useCallback } from "react";
import { parseInputLines, validateEntry } from "../utils/sequenceParser";

const EMPTY_ENTRY = { raw: ">Name\nSEQUENCE\n(((...)))" };

export default function InputPanel({ onRun, isRunning }) {
  const [entries, setEntries] = useState([{ ...EMPTY_ENTRY, id: Date.now() }]);
  const [errors, setErrors] = useState({});
  const [preview, setPreview] = useState({});

  const updateEntry = useCallback((id, raw) => {
    setEntries((prev) => prev.map((e) => (e.id === id ? { ...e, raw } : e)));

    // Live preview parse
    try {
      const parsed = parseInputLines(raw);
      setPreview((p) => ({ ...p, [id]: parsed }));
      setErrors((p) => ({ ...p, [id]: null }));
    } catch (err) {
      setPreview((p) => ({ ...p, [id]: null }));
      setErrors((p) => ({ ...p, [id]: err.message }));
    }
  }, []);

  const addEntry = () =>
    setEntries((prev) => [...prev, { raw: ">NewStruct\nSEQUENCE\n(((...)))", id: Date.now() }]);

  const removeEntry = (id) => {
    setEntries((prev) => prev.filter((e) => e.id !== id));
    setErrors((p) => { const n = { ...p }; delete n[id]; return n; });
    setPreview((p) => { const n = { ...p }; delete n[id]; return n; });
  };

  const handleRun = () => {
    const allParsed = [];
    const newErrors = {};
    let hasError = false;

    for (const entry of entries) {
      try {
        const parsed = parseInputLines(entry.raw);
        validateEntry(parsed);
        allParsed.push(parsed);
      } catch (err) {
        newErrors[entry.id] = err.message;
        hasError = true;
      }
    }

    setErrors(newErrors);
    if (!hasError) onRun(allParsed);
  };

  return (
    <div className="input-panel">
      <div className="panel-header">
        <h2 className="panel-title">Interactive Mode</h2>
        <span className="panel-hint">Format: nazwa / sekwencja / dot-bracket*</span>
      </div>

      <div className="entries-list">
        {entries.map((entry, idx) => (
          <EntryCard
            key={entry.id}
            index={idx}
            entry={entry}
            error={errors[entry.id]}
            preview={preview[entry.id]}
            onChange={(raw) => updateEntry(entry.id, raw)}
            onRemove={entries.length > 1 ? () => removeEntry(entry.id) : null}
          />
        ))}
      </div>

      <div className="panel-actions">
        <button className="btn-add" onClick={addEntry} disabled={isRunning}>
          <svg viewBox="0 0 16 16" fill="none"><line x1="8" y1="2" x2="8" y2="14" stroke="currentColor" strokeWidth="1.5"/><line x1="2" y1="8" x2="14" y2="8" stroke="currentColor" strokeWidth="1.5"/></svg>
          Dodaj strukturę
        </button>
        <button className="btn-run" onClick={handleRun} disabled={isRunning}>
          {isRunning ? (
            <>
              <SpinnerIcon />
              Obliczanie…
            </>
          ) : (
            <>
              <PlayIcon />
              RUN
            </>
          )}
        </button>
      </div>

      <style>{`
        .input-panel {
          display: flex; flex-direction: column; gap: 0; height: 100%;
        }
        .panel-header {
          padding: 1rem 1.25rem .75rem;
          border-bottom: 1px solid var(--border);
        }
        .panel-title {
          font-family: var(--font-mono); font-size: .85rem; font-weight: 700;
          color: var(--accent); text-transform: uppercase; letter-spacing: .1em;
          margin-bottom: .2rem;
        }
        .panel-hint {
          font-size: .7rem; color: var(--text-dim); font-family: var(--font-mono);
        }
        .entries-list {
          flex: 1; overflow-y: auto; padding: .75rem;
          display: flex; flex-direction: column; gap: .75rem;
        }
        .panel-actions {
          padding: .75rem 1rem;
          border-top: 1px solid var(--border);
          display: flex; gap: .6rem;
        }
        .btn-add {
          display: flex; align-items: center; gap: .4rem;
          padding: .45rem .9rem; border-radius: 6px;
          background: transparent; border: 1px solid var(--border);
          color: var(--text-dim); font-size: .8rem; cursor: pointer;
          transition: border-color .15s, color .15s;
        }
        .btn-add svg { width: 14px; height: 14px; }
        .btn-add:hover:not(:disabled) { border-color: var(--accent); color: var(--accent); }
        .btn-add:disabled { opacity: .4; cursor: not-allowed; }
        .btn-run {
          flex: 1; display: flex; align-items: center; justify-content: center; gap: .5rem;
          padding: .5rem 1rem; border-radius: 6px; border: none;
          background: var(--accent); color: #0a0c10;
          font-family: var(--font-mono); font-size: .85rem; font-weight: 700;
          letter-spacing: .08em; cursor: pointer;
          transition: background .15s, opacity .15s;
        }
        .btn-run:hover:not(:disabled) { background: #6fffc8; }
        .btn-run:disabled { opacity: .5; cursor: not-allowed; }
        .btn-run svg { width: 14px; height: 14px; }
      `}</style>
    </div>
  );
}

/* ── Entry Card ── */
function EntryCard({ index, entry, error, preview, onChange, onRemove }) {
  const [expanded, setExpanded] = useState(true);

  return (
    <div className={`entry-card ${error ? "has-error" : ""} ${preview ? "has-preview" : ""}`}>
      <div className="entry-header" onClick={() => setExpanded((v) => !v)}>
        <span className="entry-index">#{String(index + 1).padStart(2, "0")}</span>
        <span className="entry-name">
          {preview?.name || entry.raw.split("\n")[0]?.replace(/^>/, "") || "…"}
        </span>
        <div className="entry-header-actions" onClick={(e) => e.stopPropagation()}>
          {onRemove && (
            <button className="btn-icon btn-remove" onClick={onRemove} title="Usuń">
              <svg viewBox="0 0 16 16" fill="none"><line x1="3" y1="3" x2="13" y2="13" stroke="currentColor" strokeWidth="1.5"/><line x1="13" y1="3" x2="3" y2="13" stroke="currentColor" strokeWidth="1.5"/></svg>
            </button>
          )}
          <button className="btn-icon btn-toggle" title={expanded ? "Zwiń" : "Rozwiń"}>
            <svg viewBox="0 0 16 16" fill="none" style={{ transform: expanded ? "rotate(180deg)" : "none", transition: "transform .2s" }}>
              <polyline points="3,6 8,11 13,6" stroke="currentColor" strokeWidth="1.5" fill="none"/>
            </svg>
          </button>
        </div>
      </div>

      {expanded && (
        <>
          <textarea
            className="entry-textarea"
            value={entry.raw}
            onChange={(e) => onChange(e.target.value)}
            spellCheck={false}
            rows={3}
            placeholder={">NazwaStruktury\nGGGGCCCC\n((((....))))*"}
          />

          {error && <div className="entry-error"><ErrorIcon />{error}</div>}

          {preview && !error && (
            <div className="entry-preview">
              <PreviewRow label="Twist" value={`${preview.twist}°`} />
              <PreviewRow label="Sugar" value={preview.sugarPucker} highlight={preview.sugarPucker === "N" ? "rna" : "dna"} />
              <PreviewRow label="Path" value={preview.path?.join("→") ?? "–"} mono />
            </div>
          )}
        </>
      )}

      <style>{`
        .entry-card {
          border: 1px solid var(--border); border-radius: 8px;
          background: var(--surface2); overflow: hidden;
          transition: border-color .15s;
        }
        .entry-card.has-error { border-color: var(--error); }
        .entry-card.has-preview:not(.has-error) { border-color: var(--accent-dim); }
        .entry-header {
          display: flex; align-items: center; gap: .5rem;
          padding: .5rem .75rem; cursor: pointer;
          border-bottom: 1px solid var(--border);
        }
        .entry-index {
          font-family: var(--font-mono); font-size: .65rem; color: var(--text-dim);
          min-width: 28px;
        }
        .entry-name {
          flex: 1; font-family: var(--font-mono); font-size: .78rem;
          color: var(--text); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
        }
        .entry-header-actions { display: flex; gap: .25rem; margin-left: auto; }
        .btn-icon {
          width: 22px; height: 22px; display: flex; align-items: center; justify-content: center;
          background: none; border: none; cursor: pointer; color: var(--text-dim);
          border-radius: 4px; transition: color .15s, background .15s;
        }
        .btn-icon svg { width: 12px; height: 12px; }
        .btn-remove:hover { color: var(--error); background: rgba(240,90,90,.1); }
        .btn-toggle:hover { color: var(--accent); background: rgba(74,240,180,.08); }
        .entry-textarea {
          width: 100%; resize: vertical; min-height: 72px; max-height: 200px;
          background: var(--bg); border: none; border-bottom: 1px solid var(--border);
          color: var(--text); font-family: var(--font-mono); font-size: .78rem;
          line-height: 1.6; padding: .6rem .75rem; outline: none;
        }
        .entry-textarea::placeholder { color: var(--text-dim); }
        .entry-error {
          display: flex; align-items: center; gap: .4rem;
          padding: .4rem .75rem; font-size: .72rem; color: var(--error);
          font-family: var(--font-mono); background: rgba(240,90,90,.07);
        }
        .entry-error svg { width: 13px; height: 13px; flex-shrink: 0; }
        .entry-preview {
          display: grid; grid-template-columns: repeat(3,1fr);
          gap: 0; border-top: 1px solid var(--border);
        }
      `}</style>
    </div>
  );
}

function PreviewRow({ label, value, mono, highlight }) {
  return (
    <div className={`preview-row ${highlight || ""}`}>
      <span className="preview-label">{label}</span>
      <span className={`preview-value ${mono ? "mono" : ""}`}>{value}</span>
      <style>{`
        .preview-row {
          padding: .35rem .6rem;
          display: flex; flex-direction: column; gap: .1rem;
          border-right: 1px solid var(--border);
        }
        .preview-row:last-child { border-right: none; }
        .preview-row.rna { background: rgba(74,240,180,.04); }
        .preview-row.dna { background: rgba(74,170,240,.04); }
        .preview-label { font-size: .62rem; color: var(--text-dim); text-transform: uppercase; letter-spacing: .06em; }
        .preview-value { font-size: .75rem; color: var(--accent); font-weight: 600; }
        .preview-value.mono { font-family: var(--font-mono); font-size: .7rem; word-break: break-all; }
      `}</style>
    </div>
  );
}

const PlayIcon = () => (
  <svg viewBox="0 0 16 16" fill="currentColor"><polygon points="4,2 14,8 4,14"/></svg>
);
const SpinnerIcon = () => (
  <svg viewBox="0 0 16 16" fill="none" style={{ animation: "spin .8s linear infinite" }}>
    <circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="2" strokeDasharray="24" strokeDashoffset="8" strokeLinecap="round"/>
    <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
  </svg>
);
const ErrorIcon = () => (
  <svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="1.5"/><line x1="8" y1="5" x2="8" y2="9" stroke="currentColor" strokeWidth="1.5"/><circle cx="8" cy="11.5" r=".7" fill="currentColor"/></svg>
);
