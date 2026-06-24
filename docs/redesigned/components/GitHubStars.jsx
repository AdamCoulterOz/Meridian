if (typeof document !== 'undefined' && !document.getElementById('keel-ghstars-css')) {
  const s = document.createElement('style');
  s.id = 'keel-ghstars-css';
  s.textContent = `
  .keel-ghb{font-family:var(--font-sans);font-weight:var(--fw-semibold);display:inline-flex;align-items:center;
    text-decoration:none;color:var(--text-primary);letter-spacing:-0.01em;line-height:1;cursor:pointer;
    -webkit-tap-highlight-color:transparent;user-select:none;}
  .keel-ghb:focus-visible{outline:none;box-shadow:var(--ring);border-radius:var(--radius-md);}
  .keel-ghb__ico{flex:none;display:inline-flex;}
  .keel-ghb__count{font-variant-numeric:tabular-nums;}

  /* sizes */
  .keel-ghb--sm{font-size:13px;}.keel-ghb--sm .keel-ghb__seg{height:30px;}
  .keel-ghb--md{font-size:14px;}.keel-ghb--md .keel-ghb__seg{height:36px;}

  /* minimal — icon + count, transparent (nav usage) */
  .keel-ghb--minimal{gap:7px;color:var(--text-secondary);transition:color var(--dur-fast) var(--ease-out);}
  .keel-ghb--minimal:hover{color:var(--text-primary);}

  /* outline — single bordered pill */
  .keel-ghb--outline{gap:8px;border-radius:var(--radius-md);box-shadow:inset 0 0 0 1px var(--border-default);
    background:var(--surface-base);transition:background var(--dur-fast) var(--ease-out),box-shadow var(--dur-fast) var(--ease-out);}
  .keel-ghb--outline.keel-ghb--sm{padding:0 12px;height:30px;}
  .keel-ghb--outline.keel-ghb--md{padding:0 14px;height:36px;}
  .keel-ghb--outline:hover{background:var(--surface-subtle);box-shadow:inset 0 0 0 1px var(--border-strong);}
  .keel-ghb--outline:active{transform:scale(0.98);}

  /* split — [icon + Star] | [count] two segments */
  .keel-ghb--split{border-radius:var(--radius-md);box-shadow:inset 0 0 0 1px var(--border-default);overflow:hidden;
    transition:box-shadow var(--dur-fast) var(--ease-out);}
  .keel-ghb--split:hover{box-shadow:inset 0 0 0 1px var(--border-strong);}
  .keel-ghb--split:active{transform:scale(0.98);}
  .keel-ghb__seg{display:inline-flex;align-items:center;gap:7px;padding:0 13px;}
  .keel-ghb__seg--label{background:var(--surface-subtle);transition:background var(--dur-fast) var(--ease-out);}
  .keel-ghb--split:hover .keel-ghb__seg--label{background:var(--surface-sunken);}
  .keel-ghb__seg--count{background:var(--surface-base);box-shadow:inset 1px 0 0 var(--border-default);color:var(--text-secondary);}
  `;
  document.head.appendChild(s);
}

const GH_PATH = 'M9 19c-5 1.5-5-2.5-7-3m14 6v-3.9a3.4 3.4 0 0 0-.9-2.6c3-.3 6.2-1.5 6.2-6.7a5.2 5.2 0 0 0-1.5-3.6 4.9 4.9 0 0 0-.1-3.6s-1.2-.4-3.9 1.5a13.4 13.4 0 0 0-7 0C5.6 1.7 4.4 2.1 4.4 2.1a4.9 4.9 0 0 0-.1 3.6A5.2 5.2 0 0 0 2.8 9.3c0 5.2 3.2 6.4 6.2 6.7a3.4 3.4 0 0 0-.9 2.5V22';
const STAR_PATH = 'M12 3l2.6 5.3 5.9.9-4.2 4.1 1 5.8L12 16.7 6.7 19.2l1-5.8L3.5 9.2l5.9-.9z';

function Octocat({ size }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={GH_PATH} />
    </svg>
  );
}
function Star({ size }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={STAR_PATH} />
    </svg>
  );
}

/** Format a star count: 24100 → "24.1k", 1_500_000 → "1.5M". Strings pass through. */
function formatCount(n) {
  if (typeof n !== 'number' || !isFinite(n)) return n;
  if (n >= 1e6) return +(n / 1e6).toFixed(1) + 'M';
  if (n >= 1e3) return +(n / 1e3).toFixed(1) + 'k';
  return String(n);
}

/** Keel GitHubStars — a GitHub repo star control showing the octocat + star count. */
function GitHubStars({
  repo,
  stars,
  href,
  variant = 'minimal',
  size = 'md',
  label = 'Star',
  className = '',
  ...rest
}) {
  const iconSize = size === 'sm' ? 16 : 18;
  const count = formatCount(stars);
  const url = href || (repo ? `https://github.com/${repo}` : undefined);
  const aria = repo ? `${repo} on GitHub — ${count} stars` : `${count} GitHub stars`;
  const cls = ['keel-ghb', `keel-ghb--${variant}`, `keel-ghb--${size}`, className].filter(Boolean).join(' ');

  let inner;
  if (variant === 'split') {
    inner = (
      <>
        <span className="keel-ghb__seg keel-ghb__seg--label">
          <span className="keel-ghb__ico"><Star size={iconSize} /></span>{label}
        </span>
        <span className="keel-ghb__seg keel-ghb__seg--count">
          <span className="keel-ghb__count">{count}</span>
        </span>
      </>
    );
  } else {
    inner = (
      <>
        <span className="keel-ghb__ico"><Octocat size={iconSize} /></span>
        <span className="keel-ghb__count">{count}</span>
      </>
    );
  }

  return <a className={cls} href={url} aria-label={aria} {...rest}>{inner}</a>;
}

module.exports = { GitHubStars };
