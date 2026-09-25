/* Optimize All Lecture Studio — deterministic animation stage.
 *
 * window.__build(scene) builds the DOM for one scene (or the intro/outro) and registers animations on a
 * timeline. window.__seek(t) applies the exact state at time t (seconds) and returns a string key describing
 * every animated value; equal keys mean identical frames, so the renderer can reuse the previous screenshot.
 * Nothing depends on wall-clock time: rendering is frame-accurate and reproducible.
 */
(() => {
  "use strict";

  // ---------------------------------------------------------------- timeline engine
  const EASE = {
    linear: (p) => p,
    out: (p) => 1 - Math.pow(1 - p, 3),
    out5: (p) => 1 - Math.pow(1 - p, 5),
    inOut: (p) => (p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2),
    in: (p) => p * p * p,
    back: (p) => {
      const c1 = 1.35, c3 = c1 + 1;
      return 1 + c3 * Math.pow(p - 1, 3) + c1 * Math.pow(p - 1, 2);
    },
  };
  let TL = [];
  let EL = [];
  let DURATION = 0;

  function reg(el) {
    if (el.__id === undefined) {
      el.__id = EL.length;
      EL.push(el);
    }
    return el;
  }
  /** Animate el's props from [a, b] between t0 and t0 + dur. */
  function A(el, t0, dur, props, ease = "out") {
    if (!el) return;
    reg(el);
    TL.push({ el, t0, dur: Math.max(0.001, dur), props, ease: EASE[ease] || EASE.out });
  }
  /** Standard entrance: fade + rise. */
  function enter(el, t0, { dur = 0.7, y = 26, x = 0, scale = null, ease = "out" } = {}) {
    const p = { opacity: [0, 1] };
    if (y) p.y = [y, 0];
    if (x) p.x = [x, 0];
    if (scale !== null) p.scale = [scale, 1];
    A(el, t0, dur, p, ease);
  }
  function exitAll(els, t0, dur = 0.38) {
    for (const el of els) A(el, t0, dur, { opacity: [1, 0], y: [0, -14] }, "in");
  }

  function seek(t) {
    const state = new Map();
    for (const a of TL) {
      let s = state.get(a.el);
      if (!s) state.set(a.el, (s = {}));
      const p = Math.min(1, Math.max(0, (t - a.t0) / a.dur));
      const e = a.ease(p);
      for (const k in a.props) {
        const [from, to] = a.props[k];
        if (!(k in s)) s[k] = from;
        if (t >= a.t0) s[k] = from + (to - from) * e;
      }
    }
    let key = "";
    for (const [el, s] of state) {
      let tf = "";
      if ("x" in s || "y" in s) tf += `translate(${(s.x || 0).toFixed(2)}px,${(s.y || 0).toFixed(2)}px) `;
      if ("scale" in s) tf += `scale(${s.scale.toFixed(4)}) `;
      if ("sx" in s) tf += `scaleX(${s.sx.toFixed(4)}) `;
      if ("sy" in s) tf += `scaleY(${s.sy.toFixed(4)}) `;
      if ("rot" in s) tf += `rotate(${s.rot.toFixed(3)}deg) `;
      if (tf) el.style.transform = tf;
      if ("opacity" in s) el.style.opacity = Math.max(0, Math.min(1, s.opacity)).toFixed(3);
      if ("dash" in s) {
        const len = el.__len || (el.__len = el.getTotalLength ? el.getTotalLength() : 1000);
        if (s.dash >= 0.999) {
          el.style.strokeDasharray = "none";
          el.style.strokeDashoffset = "0";
        } else {
          el.style.strokeDasharray = `${len} ${len + 2}`;
          el.style.strokeDashoffset = (len * (1 - s.dash)).toFixed(2);
        }
      }
      if ("wipe" in s) el.style.clipPath = `inset(-20px ${(100 - s.wipe).toFixed(2)}% -20px -20px)`;
      if ("blur" in s) el.style.filter = s.blur > 0.05 ? `blur(${s.blur.toFixed(2)}px)` : "none";
      if ("top" in s) el.style.top = `${s.top.toFixed(2)}px`;
      if ("h" in s) el.style.height = `${s.h.toFixed(2)}px`;
      key += `${el.__id}:${tf}|${el.style.opacity}|${el.style.strokeDashoffset || ""}|${el.style.clipPath || ""}|${el.style.filter || ""}|${el.style.top}|${el.style.height};`;
    }
    return key;
  }

  // ---------------------------------------------------------------- DOM helpers
  function h(tag, cls, html) {
    const el = document.createElement(tag);
    if (cls) el.className = cls;
    if (html !== undefined) el.innerHTML = html;
    return el;
  }
  function put(parent, el, style) {
    if (style) Object.assign(el.style, style);
    parent.appendChild(el);
    return el;
  }
  const esc = (s) => String(s ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  /** Escape, then emphasise numbers / multipliers / arrows / quoted phrases. */
  function rich(s) {
    let out = esc(s);
    out = out.replace(/(?:→|-&gt;)/g, '<span class="arrow">→</span>');
    out = out.replace(/(&lt;\s?\d[\d,.]*\s?(?:min|%|s|ms|x|×)?|[+\-−]?\d[\d,.]*\s?(?:%|×|x\b|k\b|m\b)?(?:\s?(?:min|minutes|hours|days|weeks|cities|commits|steps))?)/g,
      (m) => (/\d/.test(m) ? `<em class="n">${m}</em>` : m));
    out = out.replace(/'([^']{2,40})'/g, "<em class=\"q\">'$1'</em>");
    return out;
  }
  const pad2 = (n) => String(n).padStart(2, "0");
  const cap = (s) => { s = String(s ?? "").trim(); return s ? s[0].toUpperCase() + s.slice(1) : s; };
  const richc = (s) => rich(cap(s));
  /** Vertically centre absolutely positioned blocks inside [top, bottom] (never moves content up). */
  function centerBlock(els, top, bottom) {
    els = els.filter(Boolean);
    if (!els.length) return 0;
    const t0 = Math.min(...els.map((e) => e.offsetTop));
    const t1 = Math.max(...els.map((e) => e.offsetTop + e.offsetHeight));
    const delta = Math.round(top + (bottom - top - (t1 - t0)) / 2 - t0);
    if (delta <= 0) return 0;
    for (const e of els) e.style.top = `${e.offsetTop + delta}px`;
    return delta;
  }
  const BODY_BOTTOM = 760;

  // Brand mark (reproduces frontend/src/components/brand/Logo.tsx, viewBox 8 4 88 88).
  function markSVG({ primary = "#c9cff5", accent = "#fcb31e", cls = "" } = {}) {
    return `<svg class="${cls}" viewBox="8 4 88 88" xmlns="http://www.w3.org/2000/svg">
      <circle class="ra" cx="56" cy="52" r="30" fill="none" stroke="${accent}" stroke-width="12" transform="rotate(-90 56 52)"/>
      <circle class="rp" cx="38" cy="34" r="22" fill="none" stroke="${primary}" stroke-width="11" transform="rotate(-90 38 34)"/>
      <path class="rx" d="M27.81 41.74 A30 30 0 0 0 27.81 62.26" fill="none" stroke="${accent}" stroke-width="12"/>
    </svg>`;
  }

  // ---------------------------------------------------------------- syntax highlighting (tiny, offline)
  const KW = {
    python: "and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield print",
    js: "async await break case catch class const continue default delete do else export extends false finally for from function if import in instanceof let new null return super switch this throw true try typeof undefined var void while yield",
    bash: "if then else fi for do done while in case esac function export echo cd git npm npx pip python python3 node claude codex curl docker make",
    sql: "select from where group by order having join left right inner outer on as and or not insert into values update set delete create table index limit with union distinct count sum avg min max case when then else end",
  };
  const KWSET = {};
  for (const k in KW) KWSET[k] = new Set(KW[k].split(/\s+/).map((w) => w.toLowerCase()));
  function langFamily(lang) {
    lang = (lang || "").toLowerCase();
    if (["py", "python", "python3"].includes(lang)) return "python";
    if (["js", "javascript", "ts", "typescript", "jsx", "tsx", "mjs", "json", "jsonc"].includes(lang)) return lang.startsWith("json") ? "json" : "js";
    if (["bash", "sh", "shell", "zsh", "console", "terminal", "powershell", "ps1"].includes(lang)) return "bash";
    if (["yaml", "yml", "toml", "ini", "env", "dotenv"].includes(lang)) return "yaml";
    if (["sql"].includes(lang)) return "sql";
    if (["md", "markdown"].includes(lang)) return "md";
    return "text";
  }
  function span(cls, s) { return `<span class="${cls}">${esc(s)}</span>`; }
  function hlLine(line, fam) {
    if (fam === "text") {
      return esc(line)
        .replace(/(\{[^}]{1,40}\}|&lt;[a-z_ -]{2,30}&gt;|\[[A-Z][A-Z _-]{1,30}\])/g, '<span class="tk-ph">$1</span>')
        .replace(/^(\s*)(\(?\d+[.)])/g, '$1<span class="tk-num">$2</span>');
    }
    if (fam === "md") {
      if (/^\s*#{1,6}\s/.test(line) || /^[A-Z][\w ]{0,40}:\s/.test(line)) {
        const m = line.match(/^(\s*#{1,6}\s.*|[A-Z][\w ]{0,40}:)(.*)$/);
        return span("tk-head", m[1]) + esc(m[2] || "");
      }
      return esc(line).replace(/^(\s*)(\d+[.)]|[-*])(\s)/, '$1<span class="tk-num">$2</span>$3')
        .replace(/(`[^`]+`)/g, '<span class="tk-str">$1</span>');
    }
    const kws = KWSET[fam === "yaml" || fam === "json" ? "js" : fam] || new Set();
    let out = "";
    let i = 0;
    const n = line.length;
    let first = true;
    while (i < n) {
      const rest = line.slice(i);
      let m;
      if (fam === "bash" && first && (m = rest.match(/^\$\s/))) { out += span("tk-prompt", m[0]); i += m[0].length; continue; }
      if ((fam === "python" || fam === "bash" || fam === "yaml") && rest[0] === "#") { out += span("tk-com", rest); break; }
      if ((fam === "js" || fam === "json") && rest.startsWith("//")) { out += span("tk-com", rest); break; }
      if (fam === "sql" && rest.startsWith("--")) { out += span("tk-com", rest); break; }
      if ((m = rest.match(/^(f|r|b)?("""|'''|"(?:[^"\\]|\\.)*"?|'(?:[^'\\]|\\.)*'?|`[^`]*`?)/))) {
        const tokenText = m[0];
        const after = line.slice(i + tokenText.length);
        const isKey = (fam === "json" || fam === "yaml" || fam === "js") && /^\s*:/.test(after);
        out += span(isKey ? "tk-key" : "tk-str", tokenText); i += tokenText.length; first = false; continue;
      }
      if (fam === "yaml" && (m = rest.match(/^([\w.-]+)(\s*:)/)) && out.replace(/<[^>]+>/g, "").trim() === "") {
        out += span("tk-key", m[1]) + esc(m[2]); i += m[0].length; first = false; continue;
      }
      if (fam === "bash" && (m = rest.match(/^--?[\w-]+/)) && (i === 0 || /\s/.test(line[i - 1]))) { out += span("tk-flag", m[0]); i += m[0].length; continue; }
      if ((m = rest.match(/^\d[\d_]*(\.\d+)?([eE][+-]?\d+)?/)) && (i === 0 || !/[\w]/.test(line[i - 1]))) { out += span("tk-num", m[0]); i += m[0].length; first = false; continue; }
      if ((m = rest.match(/^[A-Za-z_][\w]*/))) {
        const w = m[0];
        const next = line.slice(i + w.length);
        if (fam === "bash" && first) out += span("tk-cmd", w);
        else if (kws.has(fam === "sql" ? w.toLowerCase() : w)) out += span("tk-kw", w);
        else if (/^\s*\(/.test(next)) out += span("tk-fn", w);
        else out += esc(w);
        i += w.length; first = false; continue;
      }
      if (/\s/.test(rest[0])) { out += rest[0]; i += 1; continue; }
      if (fam === "bash" && /[|&;]/.test(rest[0])) { out += span("tk-kw", rest[0]); i += 1; first = /[|;]/.test(rest[0]) || rest.startsWith("&&"); continue; }
      out += span("tk-dim", rest[0]); i += 1; first = false;
    }
    return out;
  }
  const FILE_FOR = { python: "optimizer.py", js: "index.ts", json: "config.json", bash: "terminal", yaml: "config.yaml", sql: "query.sql", md: "PLAN.md", text: "prompt.txt" };

  // ---------------------------------------------------------------- shared chrome
  let root, scene;
  const chromeEls = [];

  function buildBackground() {
    put(root, h("div", "bg bg-base"));
    put(root, h("div", "bg bg-glow"));
    put(root, h("div", "bg bg-grid"));
    const rings = put(root, h("div", "bg-rings", markSVG({ primary: "#ffffff", accent: "#ffffff" })));
    rings.querySelector("svg").style.cssText = "width:100%;height:100%";
  }

  function buildChrome(sc) {
    const top = put(root, h("div", "chrome-top"));
    const chap = put(top, h("div", "chap"));
    if (sc.kind === "scene") {
      put(chap, h("span", "num", pad2(sc.chapterIndex)));
      put(chap, h("span", "name", esc(sc.chapter)));
    }
    const brand = put(top, h("div", "brand"));
    brand.innerHTML = markSVG() + `<span class="word">OPTIMIZE ALL<b>ACADEMY</b></span>`;
    const bottom = put(root, h("div", "chrome-bottom"));
    bottom.innerHTML = `<span>${esc(sc.course.title)}<span class="sep">/</span>Lesson ${sc.lesson.number}: ${esc(sc.lesson.title)}</span><span></span>`;
    put(root, h("div", "progress-track"));
    // Chapter pill: cross-fade in at scene start, out at scene end (the chrome itself never flickers).
    if (sc.kind === "scene") {
      enter(chap, 0.05, { dur: 0.55, y: 0, x: -16 });
      A(chap, sc.duration - 0.36, 0.34, { opacity: [1, 0] }, "in");
    }
    chromeEls.push(top, bottom);
  }

  function buildLowerThird(sc) {
    if (sc.index === 0) return;
    const l3 = put(root, h("div", "lower3"));
    l3.innerHTML = `<div class="bar"></div><div class="body"><div class="k">Chapter ${pad2(sc.chapterIndex)} of ${pad2(sc.chapterCount)}</div><div class="v">${esc(sc.chapter)}</div></div>`;
    const t0 = 0.35, hold = Math.min(3.6, Math.max(2.2, sc.duration * 0.16));
    A(l3, t0, 0.6, { opacity: [0, 1], x: [-40, 0] }, "out5");
    A(l3.querySelector(".bar"), t0, 0.5, { sy: [0, 1] }, "out");
    A(l3, t0 + hold, 0.45, { opacity: [1, 0], x: [0, -24] }, "in");
  }

  function buildCaptions(sc) {
    if (!sc.captions || !sc.captions.length) return;
    const box = put(root, h("div", "caps"));
    for (const c of sc.captions) {
      const el = put(box, h("div", "c", esc(c.text)));
      A(el, c.start, 0.08, { opacity: [0, 1] }, "linear");
      A(el, c.end, 0.08, { opacity: [1, 0] }, "linear");
    }
  }

  // reveal time for item i (falls back to an even spread)
  function rt(i, n) {
    const r = scene.reveals || [];
    if (r[i] !== undefined && r[i] !== null) return r[i];
    const a = scene.audioStart + 0.8, b = scene.audioStart + scene.audioDuration * 0.8;
    return n <= 1 ? a : a + ((b - a) * i) / (n - 1);
  }
  function focusSeq(els, times) {
    // amber focus ring on the most recently revealed item; removed when the next one arrives / near the end
    els.forEach((el, i) => {
      const ring = put(el, h("div", "focus-ring"));
      A(ring, times[i] + 0.1, 0.45, { opacity: [0, 1] }, "out");
      const off = i + 1 < times.length ? times[i + 1] : scene.duration - 1.6;
      A(ring, off, 0.45, { opacity: [1, 0] }, "inOut");
    });
  }

  // ---------------------------------------------------------------- templates
  const T = {};

  T.title = (c, sc) => {
    c.classList.add("t-title");
    const eb = put(c, h("div", "eyebrow", `Module ${sc.module.index}<span style="color:rgba(201,207,245,.4);margin:0 14px">/</span>Lesson ${sc.lesson.number}`));
    const t1 = put(c, h("div", "h1", esc(sc.lectureTitle)));
    const titleH = t1.offsetHeight;
    const course = put(c, h("div", "course", esc(sc.course.title)), { top: `${118 + titleH + 24}px` });
    enter(eb, 0.15, { y: 14 });
    enter(t1, 0.3, { y: 34, dur: 0.9, ease: "out5" });
    enter(course, 0.55, { y: 16 });
    let y = 118 + titleH + 24 + 70;
    const chain = (sc.data && sc.data.chain) || [];
    if (chain.length) {
      const row = put(c, h("div", "chain"), { left: "0px", top: `${y}px` });
      chain.forEach((n, i) => {
        if (i) {
          const ar = put(row, h("div", "arr", "→"));
          enter(ar, 0.8 + i * 0.22, { y: 0, x: -10, dur: 0.4 });
        }
        const nd = put(row, h("div", "node", esc(n)));
        enter(nd, 0.75 + i * 0.22, { y: 12, dur: 0.5 });
      });
      y += 100;
    }
    const learn = put(c, h("div", "learn"), { top: `${y}px` });
    put(learn, h("div", "k", "In this lecture"));
    const items = sc.bullets.slice(0, 5);
    const chips = [];
    let cx = 0, cy = 46;
    items.forEach((b, i) => {
      const chip = put(learn, h("div", "chip", `<span class="dot"></span>${richc(b)}`));
      const w = chip.offsetWidth;
      if (cx + w > 1060) { cx = 0; cy += 76; }
      chip.style.left = `${cx}px`; chip.style.top = `${cy}px`;
      cx += w + 16;
      chips.push(chip);
    });
    enter(learn.querySelector(".k"), 1.0, { y: 10 });
    chips.forEach((ch, i) => enter(ch, rt(i, chips.length), { y: 18, dur: 0.55 }));
    // hero mark
    const hm = put(c, h("div", "hero-mark", markSVG({ primary: "#c9cff5", accent: "#fcb31e" })));
    const svg = hm.querySelector("svg");
    A(svg.querySelector(".ra"), 0.2, 1.4, { dash: [0, 1] }, "inOut");
    A(svg.querySelector(".rp"), 0.45, 1.3, { dash: [0, 1] }, "inOut");
    A(svg.querySelector(".rx"), 1.35, 0.4, { opacity: [0, 1] }, "out");
    A(hm, 0.1, 2.2, { scale: [0.9, 1], opacity: [0, 1] }, "out");
    return [eb, t1, course, learn, hm, ...c.querySelectorAll(".chain")];
  };

  T.bullets = (c, sc) => {
    c.classList.add("t-bullets");
    const title = put(c, h("div", "h2", rich(sc.title)));
    enter(title, 0.25, { dur: 0.8, y: 30, ease: "out5" });
    const items = (sc.data && sc.data.items) || (sc.bullets.length ? sc.bullets : sc.extras);
    const n = items.length;
    const pitfalls = sc.data && sc.data.variant === "pitfalls";
    const layout = pitfalls ? "list" : (sc.data && sc.data.layout) || "list";
    const top = 40 + title.offsetHeight + 70;
    const els = [];
    const times = items.map((_, i) => rt(i, n));
    const tryItem = sc.data && sc.data.tryItem;
    if (layout === "cards" || layout === "grid") {
      const cols = layout === "cards" ? Math.min(3, Math.max(1, n)) : 2;
      const rows = Math.ceil(n / cols);
      const gap = 36;
      const W = 1680, H = 790 - top;
      const cw = (W - gap * (cols - 1)) / cols;
      const cards = items.map((b, i) => put(c, h("div", "card b-card", `<div class="idx">${pad2(i + 1)}</div><div class="txt">${richc(b)}</div><div class="bar"></div>`), {
        left: `${(i % cols) * (cw + gap)}px`, width: `${cw}px`,
      }));
      const natural = Math.max(...cards.map((cd) => cd.offsetHeight)) + 50;
      const ch = Math.min(Math.max(layout === "cards" ? 300 : 210, natural), (H - gap * (rows - 1)) / rows);
      cards.forEach((card, i) => {
        card.style.height = `${ch}px`;
        card.style.top = `${top + Math.floor(i / cols) * (ch + gap)}px`;
        enter(card, times[i], { y: 40, dur: 0.75, ease: "out5" });
        A(card.querySelector(".bar"), times[i] + 0.25, 0.7, { sx: [0, 1] }, "out");
        els.push(card);
      });
      centerBlock(cards, top, BODY_BOTTOM);
      focusSeq(els, times);
    } else {
      const avail = (tryItem ? 600 : 740) - top + 150;
      const rowH = Math.max(84, Math.min(118, avail / Math.max(1, n)));
      items.forEach((b, i) => {
        const row = put(c, h("div", "b-row", `${pitfalls ? '<div class="x">✕</div>' : ""}<div class="idx">${pad2(i + 1)}</div><div class="txt">${richc(b)}</div><div class="rule"></div>`), {
          top: `${top + i * rowH}px`, height: `${rowH}px`, right: tryItem ? "680px" : "0px",
        });
        enter(row, times[i], { y: 0, x: -30, dur: 0.6 });
        A(row.querySelector(".rule"), times[i] + 0.1, 0.8, { sx: [0, 1] }, "out");
        els.push(row);
      });
      centerBlock(els, top, BODY_BOTTOM);
    }
    if (tryItem) {
      const card = put(c, h("div", "try-card", `<div class="k">Try this now</div><div class="v">${richc(tryItem)}</div>`), { bottom: "auto" });
      const r0 = els[0].offsetTop, r1 = els[els.length - 1].offsetTop + els[els.length - 1].offsetHeight;
      card.style.top = `${Math.round((r0 + r1) / 2 - card.offsetHeight / 2)}px`;
      enter(card, sc.data.tryAt || rt(n, n + 1), { y: 40, dur: 0.8, ease: "out5" });
      els.push(card);
    }
    if (sc.extras.length && sc.bullets.length) {
      const fn = put(c, h("div", "footnote", rich(sc.extras.join("  ·  "))));
      enter(fn, rt(n, n + 1), { y: 10 });
      els.push(fn);
    }
    return [title, ...els];
  };

  T.flow = (c, sc) => {
    c.classList.add("t-flow");
    const title = put(c, h("div", "h2", rich(sc.title)));
    enter(title, 0.25, { dur: 0.8, y: 30, ease: "out5" });
    const nodes = sc.data.nodes;
    const n = nodes.length;
    const times = nodes.map((_, i) => rt(i, n));
    const shape = sc.data.shape;
    const svgNS = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(svgNS, "svg");
    svg.setAttribute("class", "wires");
    svg.setAttribute("width", "1680"); svg.setAttribute("height", "910");
    c.appendChild(svg);
    const els = [];
    const top = 40 + title.offsetHeight + 60;
    const path = (d, t0, dur = 0.7, cls = "") => {
      const p = document.createElementNS(svgNS, "path");
      p.setAttribute("d", d); if (cls) p.setAttribute("class", cls);
      svg.appendChild(p);
      A(p, t0, dur, { dash: [0, 1] }, "inOut");
      return p;
    };
    const head = (x, y, ang, t0) => {
      const g = document.createElementNS(svgNS, "path");
      g.setAttribute("d", "M0,0 L-18,-10 L-18,10 Z");
      g.setAttribute("class", "head");
      g.setAttribute("transform", `translate(${x},${y}) rotate(${ang})`);
      svg.appendChild(g);
      A(g, t0, 0.25, { opacity: [0, 1] }, "out");
    };
    if (shape === "loop") {
      const bottom = 790;
      const cx = 840, cy = top + (bottom - top) / 2;
      const Ry = Math.max(140, (bottom - top) / 2 - 62);
      const Rx = Math.min(640, Ry * 1.8);
      const center = put(c, h("div", "loop-center", `<div class="k">The loop</div><div class="v">${esc(sc.data.centerLabel || "Repeat")}</div>`));
      center.style.left = `${cx - 250}px`; center.style.width = "500px"; center.style.top = `${cy - 50}px`;
      enter(center, 0.6, { y: 0, scale: 0.94 });
      const pts = nodes.map((_, i) => {
        const a = -Math.PI / 2 + (2 * Math.PI * i) / n;
        return [cx + Rx * Math.cos(a), cy + Ry * Math.sin(a), a];
      });
      nodes.forEach((lab, i) => {
        const [x, y] = pts[i];
        const nd = put(c, h("div", "card f-node", `<div class="step">Step ${pad2(i + 1)}</div><div class="lbl">${richc(lab)}</div>`), { width: "300px" });
        nd.style.left = `${x - 150}px`; nd.style.top = `${y - nd.offsetHeight / 2}px`;
        enter(nd, times[i], { y: 0, scale: 0.9, dur: 0.6, ease: "back" });
        els.push(nd);
        const j = (i + 1) % n;
        const a0 = pts[i][2] + 0.36, a1 = (j === 0 ? pts[j][2] + 2 * Math.PI : pts[j][2]) - 0.36;
        const steps = 24; let d = "";
        for (let k = 0; k <= steps; k++) {
          const a = a0 + ((a1 - a0) * k) / steps;
          d += `${k ? "L" : "M"}${(cx + Rx * Math.cos(a)).toFixed(1)},${(cy + Ry * Math.sin(a)).toFixed(1)} `;
        }
        const tArc = (j === 0 ? scene.audioStart + scene.audioDuration * 0.85 : times[j]) - 0.55;
        path(d, Math.max(times[i] + 0.3, tArc), 0.55, j === 0 ? "soft" : "");
      });
      focusSeq(els, times);
    } else if (shape === "converge") {
      const srcN = n - 1;
      const rowH = Math.min(185, (800 - top) / srcN);
      const target = put(c, h("div", "card f-node center", `<div class="step">Result</div><div class="lbl">${richc(nodes[n - 1])}</div>`), { width: "560px", left: "1120px" });
      const midY = top + (srcN * rowH) / 2;
      target.style.top = `${midY - target.offsetHeight / 2}px`;
      for (let i = 0; i < srcN; i++) {
        const nd = put(c, h("div", "card f-node", `<div class="step">Source ${pad2(i + 1)}</div><div class="lbl">${richc(nodes[i])}</div>`), { width: "560px", left: "0px" });
        const y = top + i * rowH + (rowH - nd.offsetHeight) / 2;
        nd.style.top = `${y}px`;
        enter(nd, times[i], { x: -30, y: 0, dur: 0.6 });
        els.push(nd);
        const y0 = y + nd.offsetHeight / 2;
        const d = `M570,${y0} C 850,${y0} 850,${midY} 1100,${midY}`;
        path(d, times[i] + 0.35, 0.8);
      }
      head(1112, midY, 0, times[srcN - 1] + 1.0);
      enter(target, times[n - 1], { y: 0, scale: 0.92, dur: 0.7, ease: "back" });
      els.push(target);
      focusSeq(els, times);
    } else {
      const gap = 70;
      const w = (1680 - gap * (n - 1)) / n;
      const cy = top + (820 - top) / 2;
      let maxH = 0;
      const nds = nodes.map((lab, i) => {
        const nd = put(c, h("div", "card f-node", `<div class="step">Step ${pad2(i + 1)}</div><div class="lbl">${richc(lab)}</div>`), { width: `${w}px`, left: `${i * (w + gap)}px` });
        maxH = Math.max(maxH, nd.offsetHeight);
        return nd;
      });
      nds.forEach((nd, i) => {
        nd.style.height = `${Math.max(220, maxH)}px`; nd.style.top = `${cy - Math.max(220, maxH) / 2}px`;
        enter(nd, times[i], { y: 30, dur: 0.65, ease: "out5" });
        if (i) {
          const x0 = i * (w + gap) - gap + 10, x1 = i * (w + gap) - 12;
          path(`M${x0},${cy} L${x1},${cy}`, times[i] - 0.1, 0.4);
          head(x1 + 8, cy, 0, times[i] + 0.2);
        }
        els.push(nd);
      });
      focusSeq(els, times);
    }
    return [title, svg, ...els, ...c.querySelectorAll(".loop-center")];
  };

  T.code = (c, sc) => {
    c.classList.add("t-code");
    const code = sc.data.code;
    const fam = langFamily(code.lang);
    const lines = code.code.split("\n");
    const maxLen = Math.max(...lines.map((l) => l.length));
    const wrapOk = fam === "text" || fam === "md";
    // Long code: wider window, narrower side column, readable font and a scrolling viewport.
    const long = lines.length > 14 || (!wrapOk && maxLen > 64);
    const sideW = long ? 440 : 560;
    const side = put(c, h("div", "side"), { width: `${sideW}px` });
    const title = put(side, h("div", "h2", rich(sc.title)));
    enter(title, 0.25, { dur: 0.8, y: 30, ease: "out5" });
    const pts = [];
    let y = title.offsetHeight + 48;
    const bl = sc.bullets.slice(0, 5);
    bl.forEach((b, i) => {
      const p = put(side, h("div", "pt", `<span class="dot"></span><span>${richc(b)}</span>`), { top: `${y}px`, width: `${sideW}px` });
      y += p.offsetHeight + 22;
      pts.push(p);
      enter(p, rt(i, bl.length), { x: -20, y: 0, dur: 0.55 });
    });
    const win = put(c, h("div", "win"));
    const bar = put(win, h("div", "bar", `<i></i><i></i><i></i><span class="fname">${esc(code.filename || FILE_FOR[fam])}</span><span class="tag lang">${esc(code.lang || "text")}</span>`));
    const body = put(win, h("div", "code"));
    const winW = long ? 1200 : 1080;
    win.style.width = `${winW}px`;
    const usable = winW - 70 - 40;
    let fs = lines.length <= 4 ? 30 : lines.length <= 8 ? 27 : 25;
    while (fs > 19 && !wrapOk && maxLen * fs * 0.6 > usable) fs -= 1;
    body.style.fontSize = `${fs}px`; body.style.lineHeight = "1.52";
    const scroller = put(body, h("div", ""), { position: "relative" });
    const hl = put(scroller, h("div", "hl"));
    const lineEls = lines.map((ln, i) => put(scroller, h("div", "ln", `<span class="no">${i + 1}</span><span class="src">${hlLine(ln, fam) || " "}</span>`)));
    const VIEW = 660;
    const overflow = scroller.offsetHeight > VIEW;
    if (overflow) {
      body.style.height = `${VIEW + 56}px`; body.style.overflow = "hidden";
      const m = "linear-gradient(180deg, transparent 0px, #000 34px, #000 calc(100% - 34px), transparent 100%)";
      body.style.webkitMaskImage = m; body.style.maskImage = m;
    }
    enter(win, 0.45, { y: 40, dur: 0.9, ease: "out5" });
    // type-on: lines wipe in during the first part of the narration
    const t0 = scene.audioStart + 0.6;
    const typeDur = Math.min(scene.audioDuration * 0.35, 0.18 * lines.length + 0.6);
    lineEls.forEach((el, i) => {
      A(el.querySelector(".src"), t0 + (typeDur * i) / Math.max(1, lines.length), 0.35, { wipe: [0, 100] }, "linear");
      A(el.querySelector(".no"), t0 + (typeDur * i) / Math.max(1, lines.length), 0.2, { opacity: [0, 1] }, "linear");
    });
    // highlight walkthrough: chunk ranges with start times (from timeline.py)
    const chunks = sc.data.highlights || [];
    const tops = lineEls.map((el) => el.offsetTop);
    const hts = lineEls.map((el) => el.offsetHeight);
    reg(hl);
    hl.style.opacity = "0";
    chunks.forEach((ch, k) => {
      const a = Math.max(0, ch.from), b = Math.min(lines.length - 1, ch.to);
      const topPx = tops[a] - 4, hPx = tops[b] + hts[b] - tops[a] + 8;
      if (k === 0) {
        A(hl, ch.at, 0.01, { top: [topPx, topPx], h: [hPx, hPx] }, "linear");
        A(hl, ch.at, 0.4, { opacity: [0, 1] }, "out");
      } else {
        const prev = chunks[k - 1];
        const pa = Math.max(0, prev.from), pb = Math.min(lines.length - 1, prev.to);
        A(hl, ch.at, 0.5, { top: [tops[pa] - 4, topPx], h: [tops[pb] + hts[pb] - tops[pa] + 8, hPx] }, "inOut");
      }
      if (overflow) {
        const want = (y) => Math.max(-(scroller.offsetHeight - VIEW), Math.min(0, -(y - 40)));
        const prevY = k === 0 ? 0 : want(tops[Math.max(0, chunks[k - 1].from)]);
        A(scroller, ch.at - 0.1, 0.7, { y: [prevY, want(topPx)] }, "inOut");
      }
      lineEls.forEach((el, i) => {
        const on = i >= a && i <= b;
        const prevOn = k === 0 ? true : i >= chunks[k - 1].from && i <= chunks[k - 1].to;
        A(el, ch.at, 0.45, { opacity: [prevOn ? 1 : 0.38, on ? 1 : 0.38] }, "inOut");
      });
    });
    if (chunks.length) {
      const last = chunks[chunks.length - 1];
      const tEnd = scene.audioStart + scene.audioDuration - 0.2;
      if (tEnd > last.at + 1) {
        A(hl, tEnd, 0.5, { opacity: [1, 0] }, "inOut");
        lineEls.forEach((el, i) => {
          const on = i >= last.from && i <= last.to;
          A(el, tEnd, 0.5, { opacity: [on ? 1 : 0.38, 1] }, "inOut");
        });
      }
    }
    side.style.height = `${y}px`;
    const wh = win.offsetHeight;
    win.style.top = `${Math.max(0, Math.round((780 - wh) / 2))}px`;
    side.style.top = `${Math.max(0, Math.round((780 - y) / 2))}px`;
    return [side, win];
  };

  T.compare = (c, sc) => {
    c.classList.add("t-compare");
    const title = put(c, h("div", "h2", rich(sc.title)));
    enter(title, 0.25, { dur: 0.8, y: 30, ease: "out5" });
    const d = sc.data;
    const top = 40 + title.offsetHeight + 70;
    const w = 790;
    const L = put(c, h("div", "card cmp", `<span class="lbl">${esc(d.left.label)}</span><div class="txt">${richc(d.left.text)}</div>`), { left: "0px", top: `${top}px`, width: `${w}px` });
    const R = put(c, h("div", "card cmp win2", `<span class="lbl">${esc(d.right.label)}</span><div class="txt">${richc(d.right.text)}</div>`), { right: "0px", top: `${top}px`, width: `${w}px` });
    const hh = Math.max(L.offsetHeight, R.offsetHeight, 330);
    L.style.height = R.style.height = `${hh}px`;
    const vs = put(c, h("div", "vs", "vs"), { left: `${840 - 48}px`, top: `${top + hh / 2 - 48}px` });
    const times = [rt(0, 2 + d.notes.length), rt(1, 2 + d.notes.length)];
    enter(L, times[0], { x: -40, y: 0, dur: 0.75, ease: "out5" });
    enter(vs, (times[0] + times[1]) / 2, { y: 0, scale: 0.6, dur: 0.5, ease: "back" });
    enter(R, times[1], { x: 40, y: 0, dur: 0.75, ease: "out5" });
    focusSeq([L, R], times);
    const els = [title, L, R, vs];
    let ny = top + hh + 60;
    d.notes.forEach((nt, i) => {
      const note = put(c, h("div", "note", `<span class="i">!</span><span>${richc(nt)}</span>`), { top: `${ny}px` });
      ny += note.offsetHeight + 18;
      enter(note, rt(2 + i, 2 + d.notes.length), { y: 16 });
      els.push(note);
    });
    centerBlock(els.slice(1), top, BODY_BOTTOM);
    return els;
  };

  T.chart = (c, sc) => {
    c.classList.add("t-chart");
    const side = put(c, h("div", "side"));
    const title = put(side, h("div", "h2", rich(sc.title)));
    enter(title, 0.25, { dur: 0.8, y: 30, ease: "out5" });
    let y = title.offsetHeight + 48;
    const bl = sc.bullets.slice(0, 5);
    bl.forEach((b, i) => {
      const p = put(side, h("div", "pt", `<span class="dot"></span><span>${richc(b)}</span>`), { top: `${y}px`, width: "620px" });
      y += p.offsetHeight + 24;
      enter(p, rt(i, bl.length), { x: -20, y: 0, dur: 0.55 });
    });
    const d = sc.data;
    const card = put(c, h("div", "card chart-card"));
    enter(card, 0.45, { y: 40, dur: 0.9, ease: "out5" });
    const W = 980, H = 720, x0 = 110, y0 = 610, x1 = 920, y1 = 120;
    const svgNS = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(svgNS, "svg");
    svg.setAttribute("width", W); svg.setAttribute("height", H);
    card.appendChild(svg);
    const mk = (tag, attrs) => { const e = document.createElementNS(svgNS, tag); for (const k in attrs) e.setAttribute(k, attrs[k]); svg.appendChild(e); return e; };
    for (let g = 1; g <= 4; g++) mk("line", { x1: x0, x2: x1, y1: y0 - (g * (y0 - y1)) / 4, y2: y0 - (g * (y0 - y1)) / 4, class: "gridl" });
    const ax = mk("path", { d: `M${x0},${y1 - 20} L${x0},${y0} L${x1 + 20},${y0}`, class: "axis" });
    A(ax, 0.8, 0.8, { dash: [0, 1] }, "inOut");
    const xl = mk("text", { x: x1 + 20, y: y0 + 48, "text-anchor": "end", class: "axl" }); xl.textContent = (d.axes[0] || "").toUpperCase();
    const yl = mk("text", { x: x0 - 20, y: y1 - 36, class: "axl" }); yl.textContent = (d.axes[1] || "").toUpperCase();
    A(xl, 1.2, 0.5, { opacity: [0, 1] }); A(yl, 1.2, 0.5, { opacity: [0, 1] });
    const colors = ["#fcb31e", "#8fb8ff", "#8fe3b6", "#ff9e7a"];
    const n = Math.max(1, d.curves || 1);
    const curveT = d.curveAt || scene.audioStart + 0.4;
    const hill = (x, max, half, s) => (max * Math.pow(x, s)) / (Math.pow(x, s) + Math.pow(half, s));
    for (let k = 0; k < n; k++) {
      const max = [0.92, 0.72, 0.55, 0.45][k], half = [0.22, 0.3, 0.18, 0.4][k], s = [1.25, 1.6, 1.1, 2][k];
      let dd = "", area = `M${x0},${y0} `;
      const pts = [];
      for (let i = 0; i <= 80; i++) {
        const xv = i / 80, yv = hill(xv, max, half, s);
        const X = x0 + xv * (x1 - x0), Y = y0 - yv * (y0 - y1);
        pts.push([X, Y]);
        dd += `${i ? "L" : "M"}${X.toFixed(1)},${Y.toFixed(1)} `;
        area += `L${X.toFixed(1)},${Y.toFixed(1)} `;
      }
      area += `L${x1},${y0} Z`;
      const ar = mk("path", { d: area, class: "area", fill: colors[k] });
      const cv = mk("path", { d: dd, class: "curve", stroke: colors[k] });
      const tk = curveT + k * 0.9;
      A(cv, tk, 1.6, { dash: [0, 1] }, "inOut");
      A(ar, tk + 0.9, 0.9, { opacity: [0, 0.18] }, "out");
      if (n > 1) {
        const [LX, LY] = pts[80];
        const lab = mk("text", { x: LX - 4, y: LY - 18, "text-anchor": "end", class: "axl", fill: colors[k] });
        lab.textContent = `CHANNEL ${String.fromCharCode(65 + k)}`;
        lab.setAttribute("style", `fill:${colors[k]}`);
        A(lab, tk + 1.3, 0.5, { opacity: [0, 1] });
        // current-spend dot sliding to equal slope
        const dot = mk("circle", { r: 11, class: "dot", cx: 0, cy: 0 });
        const i0 = [62, 18, 40, 30][k], i1 = [44, 30, 34, 36][k];
        const pos = (i) => pts[i];
        const moveAt = d.tangentAt || tk + 2;
        const [ax0, ay0] = pos(i0), [ax1, ay1] = pos(i1);
        A(dot, tk + 1.4, 0.4, { opacity: [0, 1] }, "out");
        A(dot, tk + 1.4, 0.01, { x: [ax0, ax0], y: [ay0, ay0] }, "linear");
        A(dot, Math.max(moveAt, tk + 1.9), 1.6, { x: [ax0, ax1], y: [ay0, ay1] }, "inOut");
      }
    }
    if (d.tangent && n === 1) {
      const max = 0.92, half = 0.22, s = 1.25, xv = 0.74;
      const yv = hill(xv, max, half, s);
      const eps = 0.01, slope = (hill(xv + eps, max, half, s) - yv) / eps;
      const X = x0 + xv * (x1 - x0), Y = y0 - yv * (y0 - y1);
      const dx = 0.24;
      const P = (xx) => [x0 + xx * (x1 - x0), y0 - (yv + slope * (xx - xv)) * (y0 - y1)];
      const [ax, ay] = P(xv - dx), [bx, by] = P(xv + dx);
      const tan = mk("line", { x1: ax, y1: ay, x2: bx, y2: by, class: "tan" });
      const ring = mk("circle", { cx: X, cy: Y, r: 20, class: "dotring" });
      const dot = mk("circle", { cx: X, cy: Y, r: 10, class: "dot" });
      const tt = d.tangentAt || curveT + 2.2;
      A(tan, tt, 0.8, { opacity: [0, 1] }, "out");
      A(dot, tt - 0.3, 0.4, { opacity: [0, 1] }, "out");
      A(ring, tt - 0.3, 0.6, { opacity: [0, 1] }, "out");
      const call = put(card, h("div", "tag amber callout", "marginal return = slope here"), d.annotate === "hill" ? { left: `${X - 150}px`, top: `${Y + 44}px` } : { left: `${X - 360}px`, top: `${Y - 110}px` });
      enter(call, tt + 0.3, { y: 14 });
      if (d.annotate === "hill") {
        const maxY = y0 - max * (y0 - y1);
        const hx = x0 + half * (x1 - x0), hy = y0 - hill(half, max, half, s) * (y0 - y1);
        const ml = mk("line", { x1: x0, x2: x1, y1: maxY, y2: maxY, class: "tan", style: "stroke:#8fb8ff" });
        const hl2 = mk("path", { d: `M${hx},${y0} L${hx},${hy} L${x0},${hy}`, class: "tan", style: "stroke:#8fe3b6;fill:none" });
        const t1 = d.curveAt + 2.0;
        A(ml, t1, 0.7, { opacity: [0, 1] }, "out");
        A(hl2, t1 + 1.2, 0.7, { opacity: [0, 1] }, "out");
        const lm = put(card, h("div", "tag callout", "max response"), { left: `${x0 + 20}px`, top: `${maxY - 52}px`, color: "#8fb8ff" });
        const lh = put(card, h("div", "tag callout", "half-saturation spend"), { left: `${hx + 16}px`, top: `${y0 - 58}px`, color: "#8fe3b6" });
        enter(lm, t1 + 0.2, { y: 10 });
        enter(lh, t1 + 1.4, { y: 10 });
      } else {
      // first-dollars vs later-dollars marker
      const x2v = 0.12, y2v = hill(x2v, max, half, s);
      const X2 = x0 + x2v * (x1 - x0), Y2 = y0 - y2v * (y0 - y1);
      const d2 = mk("circle", { cx: X2, cy: Y2, r: 9, class: "dot" });
      const c2 = put(card, h("div", "tag callout", "first money: steep"), { left: `${X2 + 26}px`, top: `${Y2 + 10}px` });
      A(d2, tt + 0.9, 0.4, { opacity: [0, 1] }, "out");
      enter(c2, tt + 1.0, { y: 10 });
      }
    }
    const il = put(card, h("span", "tag illu", "Illustrative"));
    enter(il, 1.4, { y: 0 });
    return [side, card];
  };

  T.keyidea = (c, sc) => {
    c.classList.add("t-keyidea");
    const eb = put(c, h("div", "eyebrow", esc(sc.title)));
    const st = put(c, h("div", "stmt", richc(sc.data.statement)));
    enter(eb, 0.2, { y: 12 });
    enter(st, 0.45, { y: 40, dur: 1.0, ease: "out5" });
    const ul = put(c, h("div", "under"), { top: `${170 + st.offsetHeight + 26}px`, width: "260px" });
    A(ul, 1.1, 0.8, { sx: [0, 1] }, "out5");
    const sup = sc.data.supporting;
    let x = 0, y = 170 + st.offsetHeight + 110;
    const pills = sup.map((s, i) => {
      const p = put(c, h("div", "pill", `<span class="ck">✓</span>${richc(s)}`));
      if (x + p.offsetWidth > 1680) { x = 0; y += 92; }
      p.style.left = `${x}px`; p.style.top = `${y}px`;
      x += p.offsetWidth + 20;
      return p;
    });
    pills.forEach((p, i) => enter(p, rt(i, pills.length), { y: 22, dur: 0.6 }));
    centerBlock([eb, st, ul, ...pills], 0, 780);
    return [eb, st, ul, ...pills];
  };

  T.case = (c, sc) => {
    c.classList.add("t-case");
    const illustrative = /\(illustrative\)/i.test(sc.title) || /illustrative/i.test(sc.narration || "");
    const cleanTitle = sc.title.replace(/\s*\(illustrative\)\s*/i, "").replace(/^(example|case study|case)\s*:\s*/i, "");
    const kind = /^\s*(example|worked example)/i.test(sc.title) ? "Worked example" : "Case study";
    const head = put(c, h("div", "head", `<span class="tag amber">${kind}</span>${illustrative ? '<span class="tag">Illustrative</span>' : ""}`));
    const title = put(c, h("div", "h2", rich(cleanTitle)));
    enter(head, 0.15, { y: 10 });
    enter(title, 0.3, { dur: 0.8, y: 30, ease: "out5" });
    const beats = sc.bullets.map((b) => cap(b.replace(/^(case|example)\s*:\s*/i, "")));
    const digitIdx = beats.map((b, i) => (/\d/.test(b) ? i : -1)).filter((i) => i >= 0);
    let statIdx = digitIdx.length === 1 && beats.length >= 3 ? digitIdx[0] : -1;
    const timeline = beats.filter((_, i) => i !== statIdx);
    const n = beats.length;
    const top = 96 + title.offsetHeight + 70;
    const els = [head, title];
    const width = statIdx >= 0 ? 960 : 1600;
    let y = top;
    const tEls = [];
    beats.forEach((b, i) => {
      if (i === statIdx) return;
      const beat = put(c, h("div", "beat", `<div class="node"></div><div class="txt">${rich(b)}</div>`), { top: `${y}px`, width: `${width}px` });
      tEls.push([beat, i, y]);
      y += beat.offsetHeight + 44;
    });
    if (tEls.length > 1) {
      const y0 = tEls[0][2] + 20, y1 = tEls[tEls.length - 1][2] + 20;
      const sp = put(c, h("div", "spine"), { left: "13px", top: `${y0}px`, height: `${y1 - y0}px` });
      A(sp, rt(tEls[0][1], n), Math.max(0.6, rt(tEls[tEls.length - 1][1], n) - rt(tEls[0][1], n)), { sy: [0, 1] }, "inOut");
      c.insertBefore(sp, tEls[0][0]);
      els.push(sp);
    }
    tEls.forEach(([el, i]) => { enter(el, rt(i, n), { x: -24, y: 0, dur: 0.6 }); els.push(el); });
    if (statIdx >= 0) {
      const st = put(c, h("div", "card stat", `<div class="k">Result</div><div class="v">${rich(beats[statIdx])}</div>`));
      st.style.top = `${top + 10}px`;
      enter(st, rt(statIdx, n), { y: 40, scale: 0.96, dur: 0.8, ease: "out5" });
      els.push(st);
    }
    centerBlock(els.slice(2), top, BODY_BOTTOM);
    return els;
  };

  T.recap = (c, sc) => {
    c.classList.add("t-recap");
    const title = put(c, h("div", "h2", rich(sc.title.replace(/\s+and next step$/i, ""))));
    enter(title, 0.25, { dur: 0.8, y: 30, ease: "out5" });
    const items = sc.data.items;
    const n = items.length;
    const top = 40 + title.offsetHeight + 60;
    const els = [title];
    const next = sc.data.next;
    const rowH = Math.min(120, (740 - top) / Math.max(1, n));
    items.forEach((it, i) => {
      const row = put(c, h("div", "rc", `<svg viewBox="0 0 54 54"><circle cx="27" cy="27" r="24" fill="rgba(252,179,30,0.14)" stroke="#fcb31e" stroke-width="3"/><path d="M16 28 L24 35 L39 19" fill="none" stroke="#fcb31e" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/></svg><div class="txt">${richc(it)}</div>`), { top: `${top + i * rowH}px`, width: next ? "900px" : "1600px" });
      const t = rt(i, n + (next ? 1 : 0));
      enter(row, t, { x: -24, y: 0, dur: 0.55 });
      A(row.querySelector("path"), t + 0.2, 0.45, { dash: [0, 1] }, "out");
      els.push(row);
    });
    if (next) {
      const card = put(c, h("div", "next", `<div class="k"><svg width="26" height="26" viewBox="0 0 26 26"><path d="M4 13 H21 M14 6 L21 13 L14 20" stroke="#1d174c" stroke-width="3.2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>Your next step</div><div class="v">${richc(next)}</div>`));
      card.style.top = `${Math.max(top, top + (Math.min(n, 5) * rowH - card.offsetHeight) / 2)}px`;
      enter(card, sc.data.nextAt || rt(n, n + 1), { y: 50, scale: 0.97, dur: 0.9, ease: "out5" });
      els.push(card);
    }
    const rows = els.slice(1, 1 + n);
    centerBlock(rows, top, BODY_BOTTOM);
    if (next) {
      const card = els[els.length - 1];
      const r0 = rows[0].offsetTop, r1 = rows[rows.length - 1].offsetTop + rows[rows.length - 1].offsetHeight;
      card.style.top = `${Math.round((r0 + r1) / 2 - card.offsetHeight / 2)}px`;
    }
    return els;
  };

  // ---------------------------------------------------------------- intro / outro
  function buildIntro(sc) {
    const st = put(root, h("div", "center-stack"));
    const mk = put(st, h("div", "intro-mark", markSVG({ primary: "#c9cff5", accent: "#fcb31e" })));
    const word = put(st, h("div", "intro-word", "OPTIMIZE ALL"));
    const tag = put(st, h("div", "intro-tag", "ACADEMY"));
    const svg = mk.querySelector("svg");
    A(svg.querySelector(".ra"), 0.15, 1.1, { dash: [0, 1] }, "inOut");
    A(svg.querySelector(".rp"), 0.35, 1.0, { dash: [0, 1] }, "inOut");
    A(svg.querySelector(".rx"), 1.05, 0.3, { opacity: [0, 1] }, "out");
    A(mk, 0.1, 1.6, { scale: [0.86, 1] }, "out");
    A(word, 0.9, 1.0, { opacity: [0, 1], y: [18, 0] }, "out5");
    A(tag, 1.25, 0.8, { opacity: [0, 1] }, "out");
    const d = sc.duration;
    A(st, d - 0.55, 0.5, { opacity: [1, 0], scale: [1, 0.97] }, "in");
  }

  function buildOutro(sc) {
    const c = put(root, h("div", "content outro"));
    const eb = put(c, h("div", "eyebrow", "Keep going"));
    const big = put(c, h("div", "big", `Continue at <span>${esc(sc.siteHost)}</span>`));
    const url = put(c, h("div", "url", esc(sc.lessonUrl)));
    enter(eb, 0.2, { y: 10 });
    enter(big, 0.35, { y: 36, dur: 0.9, ease: "out5" });
    enter(url, 0.8, { y: 12 });
    const nx = sc.nextLesson;
    const card = put(c, h("div", "card nextc", `<div class="k">${nx ? "Next lesson" : "What's next"}</div><div class="v">${esc(nx ? nx.title : "Take the final exam and earn your badge")}</div>`));
    enter(card, 1.1, { y: 40, dur: 0.9, ease: "out5" });
    const mk = put(c, h("div", "mark", markSVG({ primary: "#c9cff5", accent: "#fcb31e" })));
    const svg = mk.querySelector("svg");
    A(svg.querySelector(".ra"), 0.3, 1.3, { dash: [0, 1] }, "inOut");
    A(svg.querySelector(".rp"), 0.5, 1.2, { dash: [0, 1] }, "inOut");
    A(svg.querySelector(".rx"), 1.4, 0.3, { opacity: [0, 1] }, "out");
    const mask = put(root, h("div", "fade-out-mask"));
    A(mask, 0, 0.01, { opacity: [0, 0] }, "linear");
    A(mask, sc.duration - 0.9, 0.85, { opacity: [0, 1] }, "inOut");
  }

  function buildPoster(sc) {
    const c = put(root, h("div", "poster"));
    const glow = put(c, h("div", "p-glow"));
    const top = put(c, h("div", "p-top", `<span class="tag amber">${esc(sc.categoryLabel)}</span><span class="tag">${esc(sc.minutes)} min lecture</span>`));
    const title = put(c, h("div", "p-title", esc(sc.lectureTitle)));
    // fit the title: at most 3 lines
    let fs = 118;
    title.style.fontSize = `${fs}px`;
    while (title.offsetHeight > fs * 1.02 * 3 + 4 && fs > 70) { fs -= 4; title.style.fontSize = `${fs}px`; }
    const course = put(c, h("div", "p-course", esc(sc.course.title)));
    course.style.top = `${title.offsetTop + title.offsetHeight + 34}px`;
    const brand = put(c, h("div", "p-brand", markSVG() + `<span>OPTIMIZE ALL <b>ACADEMY</b></span>`));
    const badge = put(c, h("div", "p-badge", `<div class="ring"></div><div class="inner">${markSVG({ primary: "#c9cff5", accent: "#fcb31e" })}<div class="k">Lesson</div><div class="v">${pad2(sc.lesson.number)}</div><div class="m">Module ${sc.module.index}</div></div>`));
    return [glow, top, title, course, brand, badge];
  }

  // ---------------------------------------------------------------- entry points
  window.__build = (sc) => {
    TL = []; EL = [];
    scene = sc;
    DURATION = sc.duration;
    document.body.innerHTML = "";
    root = put(document.body, h("div", "", ""));
    root.id = "stage";
    buildBackground();
    if (sc.kind === "intro") { buildIntro(sc); return true; }
    if (sc.kind === "poster") { buildPoster(sc); return true; }
    buildChrome(sc);
    if (sc.kind === "outro") { buildOutro(sc); return true; }
    const c = put(root, h("div", "content"));
    const fn = T[sc.template] || T.bullets;
    const els = fn(c, sc) || [];
    buildLowerThird(sc);
    buildCaptions(sc);
    exitAll(els.filter(Boolean), sc.duration - 0.4);
    return true;
  };
  window.__seek = (t) => seek(t);
  window.__duration = () => DURATION;
})();
