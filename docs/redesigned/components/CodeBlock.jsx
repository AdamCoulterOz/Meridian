if (typeof document !== 'undefined' && !document.getElementById('keel-codeblock-css')) {
  const s = document.createElement('style');
  s.id = 'keel-codeblock-css';
  s.textContent = `
  .keel-cb{font-family:var(--font-mono);font-size:13px;line-height:1.65;border-radius:var(--radius-lg);overflow:hidden;font-feature-settings:"liga" 1,"calt" 1;
    /* Light defaults — every colour flows through these block-level vars so a
       single theme switch re-skins the whole block (chrome + syntax). */
    --cb-bg:var(--surface-base);--cb-fg:var(--text-primary);
    --cb-tabbar-bg:var(--surface-subtle);--cb-line:var(--border-subtle);--cb-tab-active-bg:var(--surface-base);
    --cb-meta:var(--text-tertiary);--cb-ln:var(--gray-400);
    --cb-shadow:inset 0 0 0 1px var(--border-subtle),var(--shadow-sm);
    --cb-c:#6e7781;--cb-k:var(--blue-700);--cb-s:var(--green-700);--cb-n:var(--amber-700);--cb-t:var(--blue-800);--cb-f:#8250df;
    background:var(--cb-bg);color:var(--cb-fg);box-shadow:var(--cb-shadow);}

  /* Dark var set — used by forced .keel-cb--dark and by --auto under a dark theme.
     (Component CSS, injected at runtime; the :not() here is not a token scope.) */
  .keel-cb--dark,
  :root[data-theme="dark"] .keel-cb--auto{
    --cb-bg:var(--code-bg);--cb-fg:var(--code-fg);
    --cb-tabbar-bg:rgba(255,255,255,.035);--cb-line:rgba(255,255,255,.08);--cb-tab-active-bg:var(--code-bg);
    --cb-meta:#7c7c88;--cb-ln:var(--code-comment);
    --cb-shadow:var(--hairline-top),var(--shadow-md),inset 0 0 0 1px rgba(255,255,255,.07);
    --cb-c:var(--code-comment);--cb-k:var(--code-keyword);--cb-s:var(--code-string);--cb-n:var(--code-number);--cb-t:var(--code-type);--cb-f:#d2a8ff;}
  @media (prefers-color-scheme: dark){
    :root:not([data-theme="light"]) .keel-cb--auto{
      --cb-bg:var(--code-bg);--cb-fg:var(--code-fg);
      --cb-tabbar-bg:rgba(255,255,255,.035);--cb-line:rgba(255,255,255,.08);--cb-tab-active-bg:var(--code-bg);
      --cb-meta:#7c7c88;--cb-ln:var(--code-comment);
      --cb-shadow:var(--hairline-top),var(--shadow-md),inset 0 0 0 1px rgba(255,255,255,.07);
      --cb-c:var(--code-comment);--cb-k:var(--code-keyword);--cb-s:var(--code-string);--cb-n:var(--code-number);--cb-t:var(--code-type);--cb-f:#d2a8ff;}
  }

  /* ---- Editor tab bar ---- */
  .keel-cb__tabs{display:flex;align-items:stretch;font-family:var(--font-sans);font-size:12.5px;font-weight:var(--fw-medium);background:var(--cb-tabbar-bg);border-bottom:1px solid var(--cb-line);}
  .keel-cb__tab{display:flex;align-items:center;gap:8px;padding:9px 15px;border-right:1px solid var(--cb-line);color:inherit;opacity:.55;white-space:nowrap;}
  .keel-cb__tab--active{opacity:1;background:var(--cb-tab-active-bg);box-shadow:inset 0 2px 0 var(--accent);}
  .keel-cb__dot{width:9px;height:9px;border-radius:2px;flex:none;}
  .keel-cb__lang{margin-left:auto;display:flex;align-items:center;padding:0 15px;font-family:var(--font-sans);font-size:11px;font-weight:var(--fw-semibold);letter-spacing:.04em;text-transform:uppercase;color:var(--cb-meta);}

  /* ---- Terminal bar ---- */
  .keel-cb__bar{display:flex;align-items:center;gap:8px;padding:11px 15px;border-bottom:1px solid var(--cb-line);}
  .keel-cb__light{width:11px;height:11px;border-radius:50%;flex:none;}
  .keel-cb__title{margin-left:6px;font-family:var(--font-mono);font-size:11.5px;color:var(--cb-meta);}

  /* ---- Body ---- */
  .keel-cb__scroll{overflow:auto;}
  .keel-cb__pre{margin:0;padding:16px 18px;white-space:pre;}
  .keel-cb__rows{display:table;width:100%;padding:14px 0;}
  .keel-cb__row{display:table-row;}
  .keel-cb__ln{display:table-cell;text-align:right;width:1px;white-space:nowrap;padding:0 16px 0 18px;user-select:none;color:var(--cb-ln);opacity:.8;}
  .keel-cb__code{display:table-cell;white-space:pre;padding-right:20px;width:100%;}

  /* ---- Token colors ---- */
  .keel-cb .c{color:var(--cb-c)}.keel-cb .k{color:var(--cb-k)}.keel-cb .s{color:var(--cb-s)}.keel-cb .n{color:var(--cb-n)}.keel-cb .t{color:var(--cb-t)}.keel-cb .f{color:var(--cb-f)}
  `;
  document.head.appendChild(s);
}

const LANG_DOT = {
  'C#': '#9b6dff', 'F#': '#37899b', 'TypeScript': '#3178c6', 'JavaScript': '#e6b800',
  'JSON': '#bcae3b', 'YAML': '#cb4b16', 'Bash': '#4eaa25', 'Shell': '#4eaa25',
  'XML': '#e37933', 'SQL': '#e38c00', 'HTML': '#e34c26', 'CSS': '#563d7c',
};

/** Keel CodeBlock — a code surface with terminal or IDE/editor chrome.
 *  theme: 'auto' (default) follows the page light/dark theme; 'dark'/'light' force it. */
function CodeBlock({
  chrome = 'editor',
  theme = 'auto',
  filename,
  tabs,
  lang,
  lineNumbers,
  startLine = 1,
  lines,
  code,
  className = '',
  children,
  ...rest
}) {
  const cls = ['keel-cb', `keel-cb--${theme}`, className].filter(Boolean).join(' ');
  const showNums = lineNumbers != null ? lineNumbers : chrome === 'editor';
  const dot = lang ? (LANG_DOT[lang] || 'var(--accent)') : null;

  // Resolve body content into per-line nodes when numbering is on.
  const lineNodes = lines || (code != null ? String(code).replace(/\n$/, '').split('\n') : null);

  const header = (() => {
    if (chrome === 'terminal') {
      return (
        <div className="keel-cb__bar">
          <span className="keel-cb__light" style={{ background: '#ff5f57' }} />
          <span className="keel-cb__light" style={{ background: '#febc2e' }} />
          <span className="keel-cb__light" style={{ background: '#28c840' }} />
          {filename && <span className="keel-cb__title">{filename}</span>}
        </div>
      );
    }
    if (chrome === 'editor') {
      const tabList = tabs && tabs.length ? tabs : (filename ? [{ name: filename, active: true }] : []);
      if (!tabList.length && !lang) return null;
      return (
        <div className="keel-cb__tabs">
          {tabList.map((t, i) => (
            <span key={i} className={'keel-cb__tab' + (t.active ? ' keel-cb__tab--active' : '')}>
              {dot && t.active && <span className="keel-cb__dot" style={{ background: dot }} />}
              {t.name}
            </span>
          ))}
          {lang && <span className="keel-cb__lang">{lang}</span>}
        </div>
      );
    }
    return null;
  })();

  return (
    <div className={cls} {...rest}>
      {header}
      <div className="keel-cb__scroll">
        {showNums && lineNodes ? (
          <div className="keel-cb__rows">
            {lineNodes.map((ln, i) => (
              <div key={i} className="keel-cb__row">
                <span className="keel-cb__ln">{startLine + i}</span>
                <span className="keel-cb__code">{ln}{ln === '' || ln == null ? '\u200b' : ''}</span>
              </div>
            ))}
          </div>
        ) : (
          <pre className="keel-cb__pre"><code>{children ?? code}</code></pre>
        )}
      </div>
    </div>
  );
}

module.exports = { CodeBlock };
