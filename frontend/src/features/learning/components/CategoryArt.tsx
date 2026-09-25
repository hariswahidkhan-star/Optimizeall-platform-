import type { CourseCategory } from '../api';

/**
 * Small animated inline-SVG illustrations for the academy's category tiles (decorative, aria-hidden). They draw with
 * `currentColor` (the category's colour) and animate with CSS only (academy.css `.lx-art-*`); static with reduced motion.
 */
export function CategoryArt({ category }: { category: CourseCategory }) {
  return (
    <svg className={`lx-art lx-art--${category.toLowerCase()}`} viewBox="0 0 96 72" width="96" height="72" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id={`lx-art-g-${category}`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="currentColor" stopOpacity="0.28" />
          <stop offset="1" stopColor="currentColor" stopOpacity="0.06" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="92" height="68" rx="14" fill={`url(#lx-art-g-${category})`} />
      {ART[category]}
    </svg>
  );
}

const stroke = { fill: 'none', stroke: 'currentColor', strokeWidth: 3, strokeLinecap: 'round', strokeLinejoin: 'round' } as const;

const ART: Record<CourseCategory, JSX.Element> = {
  // Neural network: nodes pulse, links shimmer.
  Ai: (
    <g>
      <path {...stroke} strokeWidth={2} className="lx-art-link" d="M24 22 L48 36 L72 22 M24 50 L48 36 L72 50 M24 22 L24 50 M72 22 L72 50" />
      {[
        [24, 22],
        [24, 50],
        [48, 36],
        [72, 22],
        [72, 50],
      ].map(([cx, cy], i) => (
        <circle key={i} className="lx-art-node" style={{ animationDelay: `${i * 180}ms` }} cx={cx} cy={cy} r={i === 2 ? 7 : 5} fill="currentColor" />
      ))}
    </g>
  ),
  // Search: bars rise under a magnifier.
  Seo: (
    <g>
      {[0, 1, 2, 3].map((i) => (
        <rect key={i} className="lx-art-bar" style={{ animationDelay: `${i * 140}ms` }} x={14 + i * 12} y={54 - (i + 1) * 8} width="8" height={(i + 1) * 8} rx="2" fill="currentColor" opacity={0.5 + i * 0.12} />
      ))}
      <g className="lx-art-float">
        <circle {...stroke} cx="66" cy="28" r="12" />
        <path {...stroke} d="M75 37 L84 46" />
      </g>
    </g>
  ),
  // Sales: an arrow climbing a trend line.
  Sales: (
    <g>
      <path {...stroke} className="lx-art-draw" d="M14 54 L34 40 L48 46 L70 24" />
      <path {...stroke} className="lx-art-float" d="M60 22 L72 22 L72 34" />
      <circle cx="34" cy="40" r="3.5" fill="currentColor" />
      <circle cx="48" cy="46" r="3.5" fill="currentColor" />
    </g>
  ),
  // Marketing: a megaphone with broadcast waves.
  Marketing: (
    <g>
      <path {...stroke} d="M18 32 L18 42 L28 42 L50 54 L50 20 L28 32 Z" fill="currentColor" fillOpacity="0.2" />
      {[0, 1, 2].map((i) => (
        <path key={i} {...stroke} className="lx-art-wave" style={{ animationDelay: `${i * 260}ms` }} d={`M${58 + i * 8} ${28 - i * 4} Q${64 + i * 10} 37 ${58 + i * 8} ${46 + i * 4}`} />
      ))}
    </g>
  ),
  // Business: a briefcase and a rising coin stack.
  Business: (
    <g>
      <rect {...stroke} x="14" y="28" width="38" height="26" rx="4" />
      <path {...stroke} d="M26 28 V22 H40 V28" />
      {[0, 1, 2].map((i) => (
        <ellipse key={i} className="lx-art-bar" style={{ animationDelay: `${i * 160}ms` }} cx="70" cy={52 - i * 8} rx="12" ry="4" fill="currentColor" opacity={0.55 + i * 0.15} />
      ))}
    </g>
  ),
  // Design: a pen drawing a curve between handles.
  Design: (
    <g>
      <path {...stroke} className="lx-art-draw" d="M14 52 C30 14, 60 64, 82 22" />
      <rect x="10" y="48" width="8" height="8" rx="2" fill="currentColor" />
      <rect x="78" y="18" width="8" height="8" rx="2" fill="currentColor" />
      <circle className="lx-art-node" cx="48" cy="38" r="4" fill="currentColor" />
    </g>
  ),
  // Data: a growing bar chart with a trend.
  Data: (
    <g>
      <path {...stroke} strokeWidth={2} d="M14 56 H84" />
      {[0, 1, 2, 3, 4].map((i) => (
        <rect key={i} className="lx-art-bar" style={{ animationDelay: `${i * 110}ms` }} x={18 + i * 13} y={54 - [14, 24, 18, 32, 38][i]!} width="8" height={[14, 24, 18, 32, 38][i]} rx="2" fill="currentColor" opacity="0.7" />
      ))}
    </g>
  ),
  // Platform: stacked layers settling into place.
  Platform: (
    <g>
      {[0, 1, 2].map((i) => (
        <path key={i} className="lx-art-layer" style={{ animationDelay: `${i * 200}ms` }} d={`M48 ${16 + i * 12} L78 ${28 + i * 12} L48 ${40 + i * 12} L18 ${28 + i * 12} Z`} fill="currentColor" fillOpacity={0.25 + i * 0.2} stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
      ))}
    </g>
  ),
};
