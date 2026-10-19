// Fantasy RPG Battles: the page side of the phone version.
//
// The battle itself runs in WebAssembly (src/Battle.Web): the same rules, content and goblin scripts
// as the terminal game, so a battle played here replays exactly in battle-sim. This file draws the
// board, turns taps into the engine's legal actions, and plays the enemies' turns back one action at
// a time. Nothing here decides a rule: every choice offered comes from the engine's legal list.

const $ = (id) => document.getElementById(id);
const SVG = 'http://www.w3.org/2000/svg';
const T = 40; // one tile, in board units
const M = 13; // room for the coordinates along the top and the left

const NAMES = {
  'first-goblins': 'First goblins',
  'goblin-camp': 'Goblin camp',
  'goblin-camp-tier2': 'Goblin camp, Tier 2',
  'river-crossing': 'River crossing',
};
const ORDER = ['first-goblins', 'goblin-camp', 'goblin-camp-tier2', 'river-crossing'];
const TIMING_LABELS = { novice: 'Novice', average: 'Average', expert: 'Expert', ask: "I'll grade each one" };
const HEIGHT_WORDS = ['no ground', 'low', 'base', 'high', 'very high'];
const STATUS_WORDS = {
  rooted: 'rooted', slowed: 'slowed', blinded: 'blinded', dazed: 'dazed', knocked_over: 'knocked over',
  taunted: 'taunted', braced: 'braced', buffed: 'spurred on', weapon_bind: 'weapon bind', parry_ready: 'parry ready',
};
const OUTCOMES = { won: 'Victory!', lost: 'Defeat', draw: 'A draw', fled: 'The party fled' };
const SLOT_WORDS = { main: 'main', bonus: 'bonus', special: 'main + bonus' };
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');

let api = null;      // the engine's exported functions
let boot = null;     // encounters, timing skills, versions
let setup = null;    // what never changes during the running battle
let view = null;     // the battle as it stands
let busy = false;    // true while the engine or an animation is running
let skipping = false; // "Skip ahead" pressed during the enemies' turn
let ui = freshUi();
let danger = null;   // { unit, tiles: Set } while a danger zone is marked
let prefs = { heights: true, pace: 'normal', ...(store('get', 'frpg.prefs') ?? {}) };
let meta = null;     // { docId, startedAt } for the running battle
let db = null;       // the artifact's store, when this page has one
let saved = '';      // '', 'saved' or a failure code, for saving battles where Claude can read them
let choice = { encounter: 'first-goblins', timing: 'average' };
let gradeAnswer = null;
let board = null;    // the SVG layers and tokens
let feedRound = null;

function freshUi() {
  return { mode: 'idle', group: null, pick: null, candidates: null, hint: '' };
}

// ------------------------------------------------------------------
// Small helpers
// ------------------------------------------------------------------

function store(op, key, value) {
  // Browser storage is a convenience: it can be missing or throw, and the page works without it.
  try {
    if (op === 'get') { const v = localStorage.getItem(key); return v ? JSON.parse(v) : null; }
    if (op === 'set') localStorage.setItem(key, JSON.stringify(value));
    if (op === 'remove') localStorage.removeItem(key);
  } catch { /* no storage here */ }
  return null;
}

function el(tag, attrs = {}, children = []) {
  const n = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k.startsWith('on')) n.addEventListener(k.slice(2), v);
    else n.setAttribute(k, v === true ? '' : String(v));
  }
  for (const c of [].concat(children)) if (c != null && c !== false) n.append(c);
  return n;
}

function sv(tag, attrs = {}, text) {
  const n = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) if (v != null) n.setAttribute(k, String(v));
  if (text != null) n.textContent = text;
  return n;
}

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const title = (id) => id.replace(/-/g, ' ').replace(/^./, (c) => c.toUpperCase());
const unitName = (i) => (i >= 0 ? setup.units[i].name : '');
const sideOf = (i) => (i >= 0 ? setup.units[i].side : '');
const idx = (x, y) => y * setup.w + x;

function show(screen) {
  for (const id of ['loading', 'start', 'battle']) $(id).hidden = id !== screen;
}

// ------------------------------------------------------------------
// The engine
// ------------------------------------------------------------------

class EngineError extends Error {
  constructor(message, detail) { super(message); this.detail = detail; }
}

function call(name, ...args) {
  const r = JSON.parse(api[name](...args));
  if (r.error) throw new EngineError(r.error, r.detail);
  return r;
}

const policyProblems = [];
addEventListener('securitypolicyviolation', (e) => {
  policyProblems.push({ directive: e.violatedDirective, blocked: e.blockedURI });
});

async function loadEngine() {
  const { dotnet } = await import('./_framework/dotnet.js');
  const runtime = await dotnet
    .withModuleConfig({ onDownloadResourceProgress: (done, total) => { $('load-bar').style.width = `${total ? Math.round((100 * done) / total) : 0}%`; } })
    .create();
  const exports = await runtime.getAssemblyExports('Battle.Web.dll');
  return exports.Battle.Web.Api;
}

function describeError(e) {
  const text = e instanceof Error ? `${e.message}${e.detail ? `\n\n${e.detail}` : ''}` : String(e);
  const policy = policyProblems.length ? `\n\nBlocked by the page's security policy: ${policyProblems.map((p) => `${p.directive} ${p.blocked}`).join('; ')}` : '';
  return text + policy;
}

function showFatal(e) {
  show('loading');
  const text = describeError(e);
  const noWasm = /WebAssembly|wasm-unsafe-eval|unsafe-eval/i.test(text);
  $('loading').querySelector('h1').textContent = noWasm ? "The battle engine can't run here" : 'Something went wrong';
  $('load-note').textContent = noWasm
    ? "The engine runs as WebAssembly, and wherever this page is open doesn't allow it. Tell Claude, who can host it somewhere that does."
    : 'Tell Claude what it says below, and it can be fixed.';
  $('load-bar').parentElement.hidden = true;
  const old = $('loading').querySelector('.problem');
  if (old) old.remove();
  $('loading').append(el('div', { class: 'problem small' }, text));
  report({ ok: false, error: text.slice(0, 4000) });
}

// The store this page can keep battles in, so Claude can read them back. Optional.
function claimStore() {
  const use = window.claude?.use;
  if (typeof use !== 'function') return;
  Promise.resolve(use.call(window.claude, 'db')).then((d) => { db = d; if (db && setup && view?.outcome === 'ongoing') queueSave(); }).catch(() => {});
}

async function report(body) {
  // A failed start is worth telling Claude about; it waits a little for the store to answer.
  for (let i = 0; i < 20 && !db; i++) await wait(500);
  if (!db) return;
  try { await db.doc('diagnostics/last-start').set({ ...body, at: new Date().toISOString(), agent: navigator.userAgent }); } catch { /* not allowed here */ }
}

// ------------------------------------------------------------------
// Starting up
// ------------------------------------------------------------------

async function main(hot) {
  claimStore();
  window.claude?.hot?.snapshot?.(() => (setup && view?.outcome === 'ongoing' ? { log: api.Log(), meta } : {}));
  api = await loadEngine();
  boot = call('Boot');
  buildStart();
  if (hot?.log) resume(hot.log, hot.meta);
  else show('start');
}

function start(hot) {
  main(hot ?? {}).catch(showFatal);
}

// ------------------------------------------------------------------
// The start screen
// ------------------------------------------------------------------

function buildStart() {
  const fights = $('fights');
  fights.textContent = '';
  const rank = (id) => (ORDER.includes(id) ? ORDER.indexOf(id) : 99);
  for (const e of [...boot.encounters].sort((a, b) => rank(a.id) - rank(b.id) || a.id.localeCompare(b.id))) {
    const chips = el('div', { class: 'chips' }, [
      ...countNames(e.enemies).map((n) => el('span', { class: 'chip enemy' }, n)),
      e.tier !== 1 ? el('span', { class: 'chip tier' }, `Elderwood Tier ${e.tier}`) : null,
    ]);
    const card = el('button', { class: 'fight', type: 'button', 'aria-pressed': String(e.id === choice.encounter), onclick: () => { choice.encounter = e.id; buildStart(); } }, [
      miniMap(e),
      el('div', {}, [el('h3', {}, NAMES[e.id] ?? title(e.id)), el('p', {}, e.about), chips]),
    ]);
    fights.append(card);
  }

  const seg = $('timing');
  seg.textContent = '';
  for (const id of [...boot.skills.map((s) => s.id), 'ask']) {
    seg.append(el('button', { type: 'button', 'aria-pressed': String(id === choice.timing), onclick: () => { choice.timing = id; buildStart(); } }, TIMING_LABELS[id] ?? title(id)));
  }
  const skill = boot.skills.find((s) => s.id === choice.timing);
  $('timing-note').textContent = skill
    ? `Until the timing minigames exist, each one is rolled for ${article(choice.timing)} ${choice.timing} player: ${skill.chances[0]}% fumble, ${skill.chances[1]}% good, ${skill.chances[2]}% perfect.`
    : 'Until the timing minigames exist, you say how each of your action commands and guards went.';

  const slot = $('resume-slot');
  slot.textContent = '';
  const s = store('get', 'frpg.battle');
  if (s?.log) {
    slot.append(el('div', { class: 'resume' }, [
      el('div', {}, [el('strong', {}, 'Battle in progress'), el('div', { class: 'small muted' }, `${NAMES[s.encounter] ?? s.encounter} · round ${s.round} · seed ${s.seed}`)]),
      el('div', { class: 'row' }, [
        el('button', { class: 'primary', type: 'button', onclick: () => resume(s.log, s.meta) }, 'Carry on'),
        el('button', { class: 'ghost', type: 'button', onclick: () => { store('remove', 'frpg.battle'); buildStart(); } }, 'Throw it away'),
      ]),
    ]));
  }
  $('build-note').textContent = `Rules ${boot.rules} · content ${boot.content}. HP shows exactly, as it will on Easy.`;
  $('start-btn').onclick = () => newBattle(choice.encounter, $('seed').value, choice.timing);
}

const article = (word) => (/^[aeiou]/i.test(word) ? 'an' : 'a');

function countNames(names) {
  const counts = new Map();
  for (const n of names) counts.set(n, (counts.get(n) ?? 0) + 1);
  return [...counts].map(([n, c]) => (c > 1 ? `${c} ${n}s` : n));
}

function miniMap(e) {
  const { w, h, map } = e;
  const svg = sv('svg', { viewBox: `0 0 ${w} ${h}`, class: 'mini', 'aria-hidden': 'true', preserveAspectRatio: 'xMidYMid meet' });
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = y * w + x;
      svg.append(sv('rect', { x, y, width: 1.02, height: 1.02, class: tileClass(boot.terrain[map.terrain[i]], map.height[i]) }));
      const f = map.feature[i] >= 0 ? boot.features[map.feature[i]] : '';
      if (f.endsWith('tree')) svg.append(sv('circle', { cx: x + 0.5, cy: y + 0.5, r: f === 'ancient_tree' ? 0.45 : 0.32, class: 'f-canopy' }));
    }
  }
  for (const [g, x, y] of map.units) {
    const party = g === 'H' || g === 'L';
    svg.append(sv('rect', { x: x + 0.12, y: y + 0.12, width: 0.76, height: 0.76, rx: 0.15, class: `tk-card ${party ? 'party' : 'enemy'}`, style: 'stroke-width:0.12' }));
  }
  return svg;
}

function tileClass(terrainId, height) {
  if (terrainId === 'no_ground') return 't-void';
  if (terrainId === 'water') return 't-water';
  if (terrainId === 'rock') return 't-rock';
  return `t-g${clamp(height, 1, 4)}`;
}

// ------------------------------------------------------------------
// Starting, resuming and leaving a battle
// ------------------------------------------------------------------

function newBattle(encounter, seed, timing) {
  const s = String(seed ?? '').trim();
  if (s && !/^\d{1,15}$/.test(s)) {
    $('seed').closest('details').open = true;
    $('seed-note').textContent = 'A seed is a whole number, like 2026. Leave it blank for a random one.';
    $('seed').focus();
    return;
  }
  try {
    const r = call('New', encounter, s, timing);
    enter(r, { docId: `${encounter}-${r.setup.seed}-${Date.now().toString(36)}`, startedAt: new Date().toISOString() });
  } catch (e) {
    if (e instanceof EngineError && !e.detail) { $('timing-note').textContent = `That battle couldn't start: ${e.message}`; return; }
    showFatal(e);
  }
}

function resume(log, savedMeta) {
  try {
    const r = call('Resume', log);
    enter(r, savedMeta ?? { docId: `${r.setup.encounter}-${r.setup.seed}-${Date.now().toString(36)}`, startedAt: new Date().toISOString() });
  } catch (e) {
    store('remove', 'frpg.battle');
    buildStart();
    show('start');
    $('resume-slot').append(el('p', { class: 'problem small' }, `That battle couldn't be picked up again: ${e.message}`));
  }
}

function enter(r, battleMeta) {
  setup = r.setup;
  view = r.view;
  meta = battleMeta;
  saved = '';
  ui = freshUi();
  danger = null;
  skipping = false;
  closeSheet();
  initBoard();
  $('feed').textContent = '';
  feedRound = null;
  feed(r.events);
  caption('Tap a lit tile to walk, or pick a move. Tap any unit or tile to inspect it.', 8000);
  show('battle');
  window.scrollTo(0, 0);
  render();
  keepLocal();
  queueSave();
  runFlow();
}

function leave() {
  closeSheet();
  setup = null;
  view = null;
  hideConfirm();
  buildStart();
  show('start');
  window.scrollTo(0, 0);
}

// ------------------------------------------------------------------
// The flow of a battle
// ------------------------------------------------------------------

/** The player's choice: an action's index from the view. */
async function act(index) {
  if (busy) return;
  busy = true;
  ui = freshUi();
  hideConfirm();
  try {
    await present(call('Act', view.v, index));
    await flow();
  } catch (e) { problem(e); } finally { busy = false; render(); }
}

/** Carries the battle on by itself, from a fresh start or a resumed battle. */
async function runFlow() {
  if (busy) return;
  busy = true;
  try { await flow(); } catch (e) { problem(e); } finally { busy = false; render(); }
}

/**
 * Carries on after any input: asks for a grade when one is waiting, plays the enemies' actions one
 * at a time, and stops when it's the party's turn again or the battle is over.
 */
async function flow() {
  for (;;) {
    if (view.outcome !== 'ongoing') { finish(); return; }
    if (view.pending) {
      render();
      const grade = await askGrade(view.pending);
      await present(call('Grade', grade));
      continue;
    }
    const actor = view.actor;
    if (actor >= 0 && sideOf(actor) === 'enemy') {
      render();
      await present(call('Step'));
      if (!skipping && prefs.pace !== 'off' && view.outcome === 'ongoing') await wait(prefs.pace === 'fast' ? 140 : 420);
      continue;
    }
    skipping = false;
    return;
  }
}

/** Shows what an engine call led to: new lines in the feed, the moves played out, then the new view. */
async function present(r) {
  const before = view;
  const events = r.events ?? [];
  feed(events);
  if (r.note) caption(r.note, 2500);
  if (before && motion()) await play(events, before);
  else { const last = [...events].reverse().find((e) => e.text); if (last) caption(last.text); }
  view = r.view;
  if (danger) danger = markDanger(danger.unit);
  render();
  keepLocal();
  if (events.some((e) => e.type === 'RoundStart' || e.type === 'BattleEnd')) queueSave();
}

const motion = () => !reducedMotion.matches && prefs.pace !== 'off' && !skipping;

function finish() {
  store('remove', 'frpg.battle');
  queueSave();
  render();
  showEnd();
}

function problem(e) {
  if (e instanceof EngineError && !e.detail) { caption(e.message, 4000); return; }
  openSheet([
    el('p', { class: 'eyebrow' }, 'Something went wrong'),
    el('h2', {}, 'The battle hit a bug'),
    el('p', {}, 'Copy this and send it to Claude, along with the battle log from the menu.'),
    el('textarea', { class: 'copybox', readonly: true, id: 'bug-text' }, describeError(e)),
    el('div', { class: 'row' }, [el('button', { class: 'ghost', type: 'button', onclick: closeSheet }, 'Close')]),
  ]);
}

// ------------------------------------------------------------------
// Keeping the battle: in this browser to survive a reload, and in the artifact's store for Claude
// ------------------------------------------------------------------

function keepLocal() {
  if (!setup) return;
  if (view.outcome !== 'ongoing') { store('remove', 'frpg.battle'); return; }
  store('set', 'frpg.battle', { log: api.Log(), meta, encounter: setup.encounter, seed: setup.seed, timing: setup.timing, round: view.round });
}

let saveChain = Promise.resolve();
let saveQueued = false;
function queueSave() {
  if (!db || !setup || !meta || saveQueued) return;
  saveQueued = true;
  saveChain = saveChain.then(async () => {
    saveQueued = false;
    if (!setup) return;
    const body = {
      encounter: setup.encounter, arena: setup.arena, seed: setup.seed, timing: setup.timing,
      rules: boot.rules, content: boot.content, round: view.round, outcome: view.outcome,
      startedAt: meta.startedAt, updatedAt: new Date().toISOString(), inputs: api.Log(),
    };
    try { await db.collection('battles').doc(meta.docId).set(body); saved = 'saved'; }
    catch (e) { saved = e?.code ?? 'failed'; }
  });
}

// ------------------------------------------------------------------
// Drawing everything
// ------------------------------------------------------------------

function render() {
  if (!setup || !view) return;
  $('round').textContent = view.round > 0 ? `Round ${view.round}` : 'Starting';
  $('turn').textContent = turnText();
  $('danger-btn').setAttribute('aria-pressed', String(!!danger));
  drawBoard();
  drawTray();
  drawRoster();
  drawConfirm();
}

function turnText() {
  if (view.outcome !== 'ongoing') return OUTCOMES[view.outcome];
  const a = view.actor;
  if (a < 0) return '';
  if (view.pending) return view.pending.need === 'guard' ? 'Guard!' : 'Action command';
  if (sideOf(a) === 'party') {
    const p = view.partner;
    return p >= 0 ? `${unitName(a)}'s turn · the ${unitName(p)}'s is still open` : `${unitName(a)}'s turn`;
  }
  return `Enemy turn · ${unitName(a)}`;
}

// ---------- The board ----------

function initBoard() {
  const svg = $('board');
  svg.textContent = '';
  const W = setup.w * T, H = setup.h * T;
  svg.setAttribute('viewBox', `${-M} ${-M} ${W + M + 2} ${H + M + 2}`);
  svg.setAttribute('aria-label', `The battle board, ${setup.w} tiles across and ${setup.h} down`);
  const defs = sv('defs');
  const hatch = sv('pattern', { id: 'hatch', width: 7, height: 7, patternUnits: 'userSpaceOnUse', patternTransform: 'rotate(45)' });
  hatch.append(sv('rect', { width: 2.6, height: 7, class: 'hatch-line' }));
  defs.append(hatch);
  svg.append(defs);
  const layer = (name) => { const g = sv('g', { class: name }); svg.append(g); return g; };
  board = {
    tiles: layer('l-tiles'), things: layer('l-things'), marks: layer('l-marks'), tethers: layer('l-tethers'),
    path: layer('l-path'), tokens: layer('l-tokens'), pops: layer('l-pops'), coords: layer('l-coords'), units: [],
  };
  for (let x = 0; x < setup.w; x++) board.coords.append(sv('text', { x: x * T + T / 2, y: -4, 'text-anchor': 'middle', class: 'coord' }, x));
  for (let y = 0; y < setup.h; y++) board.coords.append(sv('text', { x: -4, y: y * T + T / 2 + 3, 'text-anchor': 'end', class: 'coord' }, y));
  for (let i = 0; i < setup.units.length; i++) board.units.push(makeToken(i));
}

function makeToken(i) {
  const u = setup.units[i];
  const g = sv('g', { class: 'token' });
  g.append(sv('ellipse', { cx: 20, cy: 35.5, rx: 13, ry: 3.2, class: 'tk-shadow' }));
  const ring = sv('rect', { x: 4, y: 0, width: 32, height: 36, rx: 8, class: 'tk-ring pulse' });
  g.append(ring);
  g.append(sv('rect', { x: 7, y: 3, width: 26, height: 30, rx: 6, class: `tk-card ${u.side}` }));
  g.append(sv('text', { x: 20, y: 18.5, 'text-anchor': 'middle', class: 'tk-glyph' }, u.glyph));
  const hp = sv('text', { x: 20, y: 29, 'text-anchor': 'middle', class: 'tk-hp' });
  g.append(hp);
  const dot = sv('circle', { cx: 32, cy: 4, r: 3.6, class: 'tk-dot' });
  g.append(dot);
  board.tokens.append(g);
  return { g, ring, hp, dot, x: 0, y: 0 };
}

/** Where a unit's token sits within its tile: a rider sits up and left of its mount, so both show. */
function offsetOf(v, i) {
  const u = v.units[i];
  if (u.mount >= 0) return [-3, -6];
  if (u.rider >= 0) return [3, 2];
  return [0, 0];
}

function placeToken(i, x, y, v = view) {
  const t = board.units[i];
  const [ox, oy] = offsetOf(v, i);
  t.x = x; t.y = y;
  t.g.style.transform = `translate(${x * T + ox}px, ${y * T + oy}px)`;
}

function drawBoard() {
  const { w, h } = setup;
  const b = view.board;
  board.tiles.textContent = '';
  board.things.textContent = '';
  board.marks.textContent = '';
  board.tethers.textContent = '';
  board.path.textContent = '';

  // Tiles: ground shaded by height, the front face of each step drawn on its south edge.
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = idx(x, y);
      const terrain = setup.terrain[b.terrain[i]];
      const height = b.height[i];
      const cls = terrain.noGround ? 't-void' : terrain.water ? 't-water' : terrain.object ? 't-rock' : `t-g${clamp(height, 1, 4)}`;
      const X = x * T, Y = y * T;
      board.tiles.append(sv('rect', { x: X, y: Y, width: T, height: T, class: cls }));
      if (terrain.water) {
        board.tiles.append(sv('path', { d: `M${X + 7} ${Y + 15} q4 -4 8 0 t8 0`, class: 'f-ripple' }));
        board.tiles.append(sv('path', { d: `M${X + 16} ${Y + 28} q4 -4 8 0 t8 0`, class: 'f-ripple' }));
      } else if (terrain.object) {
        board.tiles.append(sv('path', { d: `M${X + 6} ${Y + 30} L${X + 15} ${Y + 12} L${X + 22} ${Y + 22} L${X + 27} ${Y + 15} L${X + 35} ${Y + 30} Z`, class: 'rock-face' }));
      } else if (terrain.ladder) {
        for (const ry of [9, 20, 31]) board.tiles.append(sv('line', { x1: X + 11, y1: Y + ry, x2: X + 29, y2: Y + ry, class: 'f-rung' }));
        board.tiles.append(sv('line', { x1: X + 11, y1: Y + 4, x2: X + 11, y2: Y + 36, class: 'f-rung' }));
        board.tiles.append(sv('line', { x1: X + 29, y1: Y + 4, x2: X + 29, y2: Y + 36, class: 'f-rung' }));
      }
      if (!terrain.noGround) {
        if (y + 1 < h) {
          const drop = height - b.height[idx(x, y + 1)];
          if (drop > 0) { const band = Math.min(drop, 3) * 4; board.tiles.append(sv('rect', { x: X, y: Y + T - band, width: T, height: band, class: 'cliff' })); }
        }
        for (const dx of [-1, 1]) {
          if (x + dx < 0 || x + dx >= w) continue;
          if (height > b.height[idx(x + dx, y)]) board.tiles.append(sv('rect', { x: dx < 0 ? X : X + T - 2, y: Y, width: 2, height: T, class: 'cliff' }));
        }
        if (prefs.heights) board.tiles.append(sv('text', { x: X + 3, y: Y + 10, class: 'hnum' }, height));
      }
      board.tiles.append(sv('rect', { x: X, y: Y, width: T, height: T, class: 't-grid' }));
    }
  }

  // Features, traps and rumbling ground.
  for (let i = 0; i < w * h; i++) {
    const x = i % w, y = Math.floor(i / w);
    if (b.feature[i] >= 0) drawFeature(setup.features[b.feature[i]], x * T + T / 2, y * T + T / 2, !!b.dead[i]);
    if (b.trap[i] === 4) board.things.append(sv('path', { d: `M${x * T + 9} ${y * T + 25} l4 -8 l4 8 l4 -8 l4 8 l4 -8 l2 4`, class: 'trap-snap' }));
    else if (b.trap[i] > 0) board.things.append(sv('path', { d: `M${x * T + 10} ${y * T + 28} q3 -16 10 -8 q6 6 10 -8`, class: 'trap-root' }));
    if (b.rumble[i] >= 0) {
      const cx = x * T + T / 2, cy = y * T + T / 2;
      board.things.append(sv('circle', { cx, cy, r: 8, class: 'rumble pulse' }));
      board.things.append(sv('circle', { cx, cy, r: 15, class: 'rumble pulse' }));
      board.things.append(sv('text', { x: cx, y: cy + 4, 'text-anchor': 'middle', class: 'rumble-glyph' }, setup.units[b.rumble[i]].glyph));
    }
  }

  // Marks: a danger zone, where the acting unit can walk, and the targets of the chosen move.
  if (danger) for (const t of danger.tiles) board.marks.append(sv('rect', { x: (t % w) * T, y: Math.floor(t / w) * T, width: T, height: T, class: 'm-danger' }));
  const choosing = view.walk !== undefined && !busy;
  if (choosing && (ui.mode === 'idle' || ui.mode === 'walk' || ui.mode === 'end')) {
    for (const o of view.walk) board.marks.append(sv('rect', { x: o.x * T + 2.5, y: o.y * T + 2.5, width: T - 5, height: T - 5, rx: 5, class: 'm-walk' }));
    if (ui.mode === 'walk' && ui.pick) drawPath(ui.pick);
  }
  if (choosing && ui.group) {
    const key = ui.mode === 'target2' ? 't2' : 't';
    const options = ui.mode === 'target2' || ui.mode === 'choose' ? ui.candidates : ui.group.options;
    const seen = new Set();
    for (const o of options) {
      const p = o[key] && targetPos(o[key]);
      if (!p || seen.has(`${p[0]},${p[1]}`)) continue;
      seen.add(`${p[0]},${p[1]}`);
      const cx = p[0] * T + T / 2, cy = p[1] * T + T / 2;
      if (o[key].k === 'unit') board.marks.append(sv('circle', { cx, cy: cy - 1, r: 19, class: 'm-target pulse' }));
      else {
        board.marks.append(sv('rect', { x: p[0] * T + 5, y: p[1] * T + 5, width: T - 10, height: T - 10, rx: 4, class: 'm-target' }));
        board.marks.append(sv('circle', { cx, cy, r: 3, class: 'm-target-dot' }));
      }
    }
    if (ui.mode === 'target2' && ui.first) board.marks.append(sv('rect', { x: ui.first[0] * T + 1.5, y: ui.first[1] * T + 1.5, width: T - 3, height: T - 3, rx: 6, class: 'm-pick' }));
    if (ui.mode === 'confirm' && ui.pick) {
      for (const key2 of ['t', 't2']) {
        const p = ui.pick[key2] && targetPos(ui.pick[key2]);
        if (p) board.marks.append(sv('rect', { x: p[0] * T + 1.5, y: p[1] * T + 1.5, width: T - 3, height: T - 3, rx: 6, class: 'm-pick' }));
      }
    }
  }

  // Tethers.
  for (const t of view.tethers) {
    board.tethers.append(sv('line', { x1: t.a[0] * T + T / 2, y1: t.a[1] * T + T / 2, x2: t.b[0] * T + T / 2, y2: t.b[1] * T + T / 2, class: 'tether' }));
  }

  // Tokens.
  for (let i = 0; i < setup.units.length; i++) {
    const u = view.units[i];
    const t = board.units[i];
    const visible = u.alive && u.on;
    t.g.style.opacity = visible ? '1' : '0';
    if (visible) placeToken(i, u.x, u.y);
    t.hp.textContent = u.hp;
    t.dot.style.display = u.status && !/^(waiting for orders|after .*)$/.test(u.status) ? '' : 'none';
    t.ring.style.display = i === view.actor && view.outcome === 'ongoing' ? '' : 'none';
  }
  // Riders draw above their mounts.
  for (let i = 0; i < setup.units.length; i++) if (view.units[i].mount >= 0) board.tokens.append(board.units[i].g);
}

function drawPath(o) {
  const a = view.units[view.actor];
  const pts = [[a.x, a.y], ...o.path].map(([x, y]) => `${x * T + T / 2},${y * T + T / 2}`).join(' ');
  board.path.append(sv('polyline', { points: pts, class: 'path' }));
  board.path.append(sv('rect', { x: o.x * T + 1.5, y: o.y * T + 1.5, width: T - 3, height: T - 3, rx: 6, class: 'm-pick' }));
}

function drawFeature(f, cx, cy, dead) {
  const g = sv('g', { class: dead ? 'dead' : '' });
  const add = (n) => g.append(n);
  switch (f.id) {
    case 'ancient_tree':
      add(sv('ellipse', { cx: cx + 1.5, cy: cy + 4, rx: 16, ry: 14, class: 'f-shadow' }));
      add(sv('circle', { cx, cy, r: 16, class: 'f-canopy' }));
      add(sv('circle', { cx: cx - 5, cy: cy - 5, r: 7.5, class: 'f-canopy-hi' }));
      add(sv('circle', { cx: cx + 6, cy: cy + 3, r: 4.5, class: 'f-canopy-hi' }));
      break;
    case 'young_tree':
      add(sv('ellipse', { cx: cx + 1.5, cy: cy + 3, rx: 11, ry: 10, class: 'f-shadow' }));
      add(sv('circle', { cx, cy, r: 11, class: 'f-canopy' }));
      add(sv('circle', { cx: cx - 3.5, cy: cy - 3.5, r: 5, class: 'f-canopy-hi' }));
      break;
    case 'thicket':
      for (const [dx, dy, r] of [[-9, -6, 6], [3, -9, 5.5], [9, 2, 6.5], [-4, 6, 6], [-11, 6, 4.5], [7, 11, 4]]) add(sv('circle', { cx: cx + dx, cy: cy + dy, r, class: 'f-leaf' }));
      break;
    case 'moss':
      for (const [dx, dy] of [[-10, -8], [-3, -11], [6, -7], [11, 2], [-8, 4], [2, 3], [-2, 11], [9, 11]]) add(sv('circle', { cx: cx + dx, cy: cy + dy, r: 2.6, class: 'f-leaf' }));
      break;
    case 'meadow':
      for (const [dx, dy] of [[-9, -6], [4, -10], [10, 3], [-6, 8], [2, 0]]) add(sv('circle', { cx: cx + dx, cy: cy + dy, r: 2.2, class: 'f-flower' }));
      break;
    case 'stream':
      add(sv('path', { d: `M${cx - 14} ${cy - 8} q5 -5 10 0 t10 0 t10 0`, class: 'f-stream' }));
      add(sv('path', { d: `M${cx - 16} ${cy + 6} q5 -5 10 0 t10 0 t10 0`, class: 'f-stream' }));
      break;
    case 'fireflies':
      for (const [dx, dy] of [[-8, -6], [6, -9], [9, 6], [-5, 8]]) {
        add(sv('circle', { cx: cx + dx, cy: cy + dy, r: 4.5, class: 'f-glow' }));
        add(sv('circle', { cx: cx + dx, cy: cy + dy, r: 1.8, class: 'f-fly' }));
      }
      break;
    case 'birds':
      for (const [dx, dy] of [[-8, -4], [5, -9], [6, 5]]) add(sv('path', { d: `M${cx + dx - 5} ${cy + dy - 3} q2.5 3 5 3 q2.5 0 5 -3`, class: 'f-bird' }));
      break;
    case 'deer':
      add(sv('path', { d: `M${cx} ${cy + 10} L${cx} ${cy - 2} M${cx} ${cy + 2} L${cx - 7} ${cy - 8} M${cx} ${cy + 2} L${cx + 7} ${cy - 8} M${cx - 4} ${cy - 4} L${cx - 9} ${cy - 3} M${cx + 4} ${cy - 4} L${cx + 9} ${cy - 3}`, class: 'f-deer' }));
      break;
    default:
      add(sv('text', { x: cx, y: cy + 5, 'text-anchor': 'middle', class: 'f-glyph' }, f.glyph));
  }
  board.things.append(g);
}

function targetPos(t) {
  if (!t) return null;
  if (t.k === 'unit') { const u = view.units[t.u]; return u.on ? [u.x, u.y] : null; }
  if (t.k === 'tile') return [t.x, t.y];
  return null;
}

function markDanger(unit) {
  const r = call('Danger', unit);
  return { unit, tiles: new Set(r.tiles) };
}

// ---------- Playing events back ----------

async function play(events, before) {
  const k = prefs.pace === 'fast' ? 0.45 : 1;
  for (let i = 0; i < setup.units.length; i++) {
    const u = before.units[i];
    if (u.alive && u.on) placeToken(i, u.x, u.y, before);
  }
  for (const e of events) {
    if (skipping) return;
    if (e.text) caption(e.text);
    switch (e.type) {
      case 'RoundStart':
        banner(`Round ${e.n}`);
        break;
      case 'Move':
        if (e.t >= 0 && e.path) await walk(e.t, e.path, 105 * k, before);
        break;
      case 'Charge': case 'Push': case 'Pull': case 'Drag': case 'Swap': case 'Thrown':
        if (e.t >= 0 && e.to) await slide(e.t, e.to, 180 * k, before);
        break;
      case 'Dismount':
        if (e.a >= 0 && e.to) await slide(e.a, e.to, 180 * k, before);
        break;
      case 'Damage':
        if (e.t >= 0) { pop(e.t, `−${e.n}`, sideOf(e.t) === 'party' ? 'pop-hurt' : 'pop-hit'); await wait(300 * k); }
        break;
      case 'Heal':
        if (e.t >= 0) { pop(e.t, `+${e.n}`, 'pop-heal'); await wait(220 * k); }
        break;
      case 'Miss': case 'Parry':
        if (e.t >= 0) { pop(e.t, e.type === 'Miss' ? 'miss' : 'parry', 'pop-word'); await wait(260 * k); }
        break;
      case 'Status':
        if (e.t >= 0) { pop(e.t, STATUS_WORDS[e.detail] ?? (e.detail ?? '').replace(/_/g, ' '), 'pop-word'); await wait(180 * k); }
        break;
      case 'Death': case 'KnockedOut': case 'Fled': case 'Burrow':
        if (e.t >= 0) { board.units[e.t].g.style.opacity = '0'; await wait(220 * k); }
        break;
      case 'Surface':
        if (e.t >= 0 && e.to) { placeToken(e.t, e.to[0], e.to[1], before); board.units[e.t].g.style.opacity = '1'; await wait(220 * k); }
        break;
      default:
        break;
    }
  }
}

/** A unit and whoever moves with it: its rider, or the mount it rides. */
function together(v, i) {
  const u = v.units[i];
  return [i, ...(u.rider >= 0 ? [u.rider] : []), ...(u.mount >= 0 ? [u.mount] : [])];
}

async function walk(i, path, stepMs, v) {
  const [sx, sy] = [board.units[i].x, board.units[i].y];
  const [ex, ey] = path[path.length - 1];
  const anims = together(v, i).map((j) => {
    const [ox, oy] = offsetOf(v, j);
    const frames = [[sx, sy], ...path].map(([x, y]) => ({ transform: `translate(${x * T + ox}px, ${y * T + oy}px)` }));
    return [j, board.units[j].g.animate(frames, { duration: stepMs * path.length, easing: 'linear' })];
  });
  await Promise.all(anims.map(([, a]) => a.finished.catch(() => {})));
  for (const [j, a] of anims) { placeToken(j, ex, ey, v); a.cancel(); }
}

async function slide(i, to, ms, v) {
  const [sx, sy] = [board.units[i].x, board.units[i].y];
  const anims = together(v, i).map((j) => {
    const [ox, oy] = offsetOf(v, j);
    return [j, board.units[j].g.animate([
      { transform: `translate(${sx * T + ox}px, ${sy * T + oy}px)` },
      { transform: `translate(${to[0] * T + ox}px, ${to[1] * T + oy}px)` },
    ], { duration: ms, easing: 'ease-out' })];
  });
  await Promise.all(anims.map(([, a]) => a.finished.catch(() => {})));
  for (const [j, a] of anims) { placeToken(j, to[0], to[1], v); a.cancel(); }
}

function pop(i, text, cls) {
  const t = board.units[i];
  const n = sv('text', { x: t.x * T + T / 2, y: t.y * T + 6, 'text-anchor': 'middle', class: `pop ${cls}` }, text);
  board.pops.append(n);
  setTimeout(() => n.remove(), 1000);
}

let captionTimer = 0;
/** The line under the board: what just happened, or why a tap did nothing. It fades once it's old news. */
function caption(text, ms = 3200) {
  const c = $('caption');
  c.textContent = text;
  c.classList.remove('stale');
  clearTimeout(captionTimer);
  captionTimer = setTimeout(() => c.classList.add('stale'), ms);
}

let bannerTimer = 0;
function banner(text) {
  const b = $('banner');
  b.textContent = text;
  b.hidden = false;
  clearTimeout(bannerTimer);
  bannerTimer = setTimeout(() => { b.hidden = true; }, 1000);
}

// ---------- The feed ----------

function feed(events) {
  const list = $('feed');
  for (const e of events) {
    if (e.type === 'RoundStart') {
      feedRound = el('ol', { class: 'feed' });
      list.prepend(el('li', {}, [el('div', { class: 'feed-round' }, `Round ${e.n}`), feedRound]));
      continue;
    }
    if (!feedRound) { feedRound = el('ol', { class: 'feed' }); list.prepend(el('li', {}, feedRound)); }
    let li = null;
    if (e.type === 'TurnStart' && e.phase === 'enemy' && e.a >= 0) li = el('li', { class: 'turn' }, `${unitName(e.a)}'s turn`);
    else if (e.text) {
      const cls = e.type === 'BattleEnd' ? 'end' : e.type === 'Damage' && e.t >= 0 ? (sideOf(e.t) === 'party' ? 'hit' : 'party-hit') : '';
      li = el('li', { class: cls }, e.text);
    }
    if (li) feedRound.append(li);
  }
  while (list.children.length > 40) list.lastChild.remove();
}

// ---------- The tray: the acting unit's choices ----------

function drawTray() {
  const tray = $('tray');
  tray.textContent = '';
  if (view.outcome !== 'ongoing') {
    tray.append(el('div', { class: 'waiting' }, [el('strong', {}, OUTCOMES[view.outcome]), el('button', { class: 'ghost', type: 'button', onclick: showEnd }, 'See how it went')]));
    return;
  }
  if (view.pending) { tray.append(el('div', { class: 'waiting' }, 'Waiting for your grade.')); return; }
  if (view.walk === undefined) {
    tray.append(el('div', { class: 'waiting' }, [
      el('span', {}, view.actor >= 0 ? `${unitName(view.actor)} is acting.` : 'The enemies are acting.'),
      el('button', { class: 'ghost', type: 'button', disabled: skipping, onclick: () => { skipping = true; render(); } }, skipping ? 'Skipping…' : 'Skip ahead'),
    ]));
    return;
  }

  const actor = view.actor;
  const swap = view.groups.find((g) => g.kind === 'swap');
  const who = el('div', { class: 'who' });
  for (const label of ['hero', 'loper']) {
    const i = setup.units.findIndex((u) => u.label === label);
    if (i < 0) continue;
    const u = view.units[i];
    const current = i === actor;
    const canSwap = !current && swap && swap.options[0].t?.u === i;
    let status;
    if (!u.alive) status = [u.gone];
    else if (!u.open) status = [`${u.hp}/${u.max} HP`, 'Turn ended'];
    else status = [`${u.hp}/${u.max} HP · ${u.walk} to walk`, `Main ${u.main ? 'ready' : 'used'} · bonus ${u.bonus ? 'ready' : 'used'}`];
    who.append(el('button', {
      type: 'button', 'aria-current': current ? 'true' : null, disabled: !canSwap,
      onclick: canSwap && !busy ? () => act(swap.options[0].i) : null,
    }, [
      el('span', { class: 'nm' }, current ? setup.units[i].name : canSwap ? `Swap to the ${setup.units[i].name}` : setup.units[i].name),
      ...status.map((line) => el('span', { class: 'st' }, line)),
    ]));
  }
  tray.append(who);

  const moves = el('div', { class: 'moves' });
  const byName = new Map(view.groups.filter((g) => g.kind === 'move').map((g) => [g.label, g]));
  for (const k of setup.units[actor].kit) {
    const g = byName.get(k.name);
    moves.append(el('button', {
      type: 'button', class: `move${g ? '' : ' off'}`, 'aria-pressed': String(!!g && ui.group === g),
      onclick: () => (g ? pickGroup(g) : explain(k)),
    }, [el('span', {}, k.name), el('span', { class: 'slot' }, SLOT_WORDS[k.slot] ?? k.slot)]));
  }
  for (const g of view.groups.filter((x) => x.kind === 'mount' || x.kind === 'dismount' || x.kind === 'flee')) {
    moves.append(el('button', { type: 'button', class: 'move', 'aria-pressed': String(ui.group === g), onclick: () => pickGroup(g) }, g.label));
  }
  if (view.end >= 0) {
    moves.append(el('button', { type: 'button', class: 'move end', 'aria-pressed': String(ui.mode === 'end'), onclick: () => { if (!busy) { ui = { ...freshUi(), mode: ui.mode === 'end' ? 'idle' : 'end' }; render(); } } }, 'End turn'));
  }
  tray.append(moves);
  if (view.sources) tray.append(el('p', { class: 'sources' }, view.sources));
}

function explain(k) {
  const u = view.units[view.actor];
  const needsMain = k.slot === 'main' || k.slot === 'special';
  const needsBonus = k.slot === 'bonus' || k.slot === 'special';
  const why = needsMain && !u.main ? 'its main action is used this turn' : needsBonus && !u.bonus ? 'its bonus action is used this turn' : 'nothing is in reach for it right now';
  caption(`${k.name} isn't possible: ${why}.`, 3000);
}

function pickGroup(g) {
  if (busy) return;
  if (ui.group === g) { ui = freshUi(); render(); return; }
  ui = g.options.length === 1 ? { ...freshUi(), mode: 'confirm', group: g, pick: g.options[0] } : { ...freshUi(), mode: 'target', group: g };
  render();
}

// ---------- Taps on the board ----------

function tileAt(ev) {
  const svg = $('board');
  const p = svg.createSVGPoint();
  p.x = ev.clientX; p.y = ev.clientY;
  const q = p.matrixTransform(svg.getScreenCTM().inverse());
  const x = Math.floor(q.x / T), y = Math.floor(q.y / T);
  return x >= 0 && y >= 0 && x < setup.w && y < setup.h ? [x, y] : null;
}

function unitAt(x, y) {
  let found = -1;
  for (let i = 0; i < view.units.length; i++) {
    const u = view.units[i];
    if (u.alive && u.on && u.x === x && u.y === y && (found < 0 || u.mount >= 0)) found = i;
  }
  return found;
}

function onTap(ev) {
  if (!setup || busy) return;
  const at = tileAt(ev);
  if (!at) return;
  const [x, y] = at;
  const choosing = view.walk !== undefined;
  if (choosing && ui.group) { tapTarget(x, y); return; }
  if (choosing) {
    const w = view.walk.find((o) => o.x === x && o.y === y);
    if (w) {
      if (ui.mode === 'walk' && ui.pick === w) { act(w.i); return; }
      ui = { ...freshUi(), mode: 'walk', pick: w };
      render();
      return;
    }
  }
  const unit = unitAt(x, y);
  if (unit >= 0) inspectUnit(unit);
  else inspectTile(x, y);
}

function tapTarget(x, y) {
  const at = (o, key) => { const p = o[key] && targetPos(o[key]); return p && p[0] === x && p[1] === y; };
  if (ui.mode === 'confirm' && ui.pick && (at(ui.pick, ui.pick.t2 ? 't2' : 't'))) { act(ui.pick.i); return; }
  if (ui.mode === 'target2') {
    const hits = ui.candidates.filter((o) => at(o, 't2'));
    if (hits.length === 1) { ui = { ...ui, mode: 'confirm', pick: hits[0] }; render(); return; }
    if (hits.length > 1) { ui = { ...ui, mode: 'choose', candidates: hits }; render(); return; }
    ui = { ...ui, hint: "That isn't somewhere the other end can go." };
    render();
    return;
  }
  const hits = ui.group.options.filter((o) => at(o, 't'));
  if (hits.length === 0) {
    ui = { ...ui, mode: ui.mode === 'confirm' ? 'target' : ui.mode, pick: ui.mode === 'confirm' ? null : ui.pick, hint: `That isn't a target for ${ui.group.label}.` };
    if (ui.group.options.length === 1) ui = freshUi();
    render();
    return;
  }
  if (hits.length === 1) { ui = { ...ui, mode: 'confirm', pick: hits[0], hint: '' }; render(); return; }
  if (hits.some((o) => o.t2)) { ui = { ...ui, mode: 'target2', candidates: hits, first: [x, y], hint: '' }; render(); return; }
  ui = { ...ui, mode: 'choose', candidates: hits, hint: '' };
  render();
}

// ---------- The confirm bar ----------

function hideConfirm() { $('confirm').hidden = true; }

function drawConfirm() {
  const bar = $('confirm');
  const inner = $('confirm-inner');
  inner.textContent = '';
  if (busy || view.walk === undefined || ui.mode === 'idle') { bar.hidden = true; return; }
  const cancel = el('button', { class: 'alt', type: 'button', onclick: () => { ui = freshUi(); render(); } }, 'Cancel');
  const add = (...nodes) => inner.append(...nodes);
  const actorName = unitName(view.actor);

  if (ui.mode === 'walk') {
    add(el('div', { class: 'what' }, `Walk the ${actorName} to (${ui.pick.x},${ui.pick.y}): ${ui.pick.cost}`),
      el('div', { class: 'hint' }, 'Tap the tile again, or Walk.'),
      el('div', { class: 'row' }, [el('button', { class: 'go', type: 'button', onclick: () => act(ui.pick.i) }, 'Walk'), cancel]));
  } else if (ui.mode === 'end') {
    const p = view.partner;
    add(el('div', { class: 'what' }, `End the ${actorName}'s turn?`),
      el('div', { class: 'hint' }, p >= 0 ? `The ${unitName(p)} can still act afterwards.` : 'That ends the party\'s turn, and the enemies move.'),
      el('div', { class: 'row' }, [el('button', { class: 'go', type: 'button', onclick: () => act(view.end) }, 'End turn'), cancel]));
  } else if (ui.mode === 'confirm') {
    const g = ui.group;
    const hint = g.kind === 'flee' ? 'The battle ends here, as a retreat.'
      : g.command ? (setup.timing === 'ask' ? "You'll say how the action command went." : `The action command is rolled for ${article(setup.timing)} ${setup.timing} player.`)
      : '';
    const onBoard = targetPos(ui.pick.t);
    add(el('div', { class: 'what' }, ui.pick.text),
      hint ? el('div', { class: 'hint' }, onBoard ? `${hint} Tap the target again, or use the button.` : hint) : null,
      el('div', { class: 'row' }, [el('button', { class: 'go', type: 'button', onclick: () => act(ui.pick.i) }, g.kind === 'flee' ? 'Flee' : g.label), cancel]));
  } else if (ui.mode === 'target' || ui.mode === 'target2') {
    const g = ui.group;
    const offBoard = ui.mode === 'target' ? g.options.filter((o) => !targetPos(o.t)) : [];
    add(el('div', { class: 'what' }, ui.mode === 'target2' ? `${g.label}: now tap where the other end goes.` : `${g.label}: tap a ringed target.`),
      el('div', { class: 'hint' }, ui.hint || g.text || ''),
      el('div', { class: 'row' }, [
        ...offBoard.map((o) => el('button', { class: 'alt', type: 'button', onclick: () => { ui = { ...ui, mode: 'confirm', pick: o, hint: '' }; render(); } }, o.t?.k === 'weapon' ? 'On your weapon' : o.t?.k === 'self' ? 'On yourself' : o.text)),
        ui.mode === 'target2' ? el('button', { class: 'alt', type: 'button', onclick: () => { ui = { ...ui, mode: 'target', candidates: null, first: null }; render(); } }, 'Back') : null,
        cancel,
      ]));
  } else if (ui.mode === 'choose') {
    add(el('div', { class: 'what' }, `${ui.group.label}: which one?`),
      el('div', { class: 'row' }, [...ui.candidates.map((o) => el('button', { class: 'alt', type: 'button', onclick: () => act(o.i) }, o.text)), cancel]));
  }
  bar.hidden = inner.childElementCount === 0;
}

// ---------- The roster ----------

function drawRoster() {
  const list = $('roster');
  list.textContent = '';
  for (let i = 0; i < setup.units.length; i++) {
    const s = setup.units[i], u = view.units[i];
    const pct = u.max ? Math.round((100 * u.hp) / u.max) : 0;
    const bar = el('span', { class: 'hpbar' }, el('span', { style: `width:${pct}%;background:var(--${s.side === 'party' ? 'party' : 'enemy'})` }));
    list.append(el('button', { class: 'unit-row', type: 'button', onclick: () => inspectUnit(i) }, [
      el('span', { class: `badge ${u.alive ? s.side : 'gone'}` }, s.glyph),
      el('span', { class: 'info' }, [el('span', { class: 'nm' }, s.name), u.alive ? bar : null, el('span', { class: 'st' }, u.alive ? u.status : u.gone)]),
      el('span', { class: 'hp' }, u.alive ? `${u.hp}/${u.max}` : '–'),
    ]));
  }
}

// ------------------------------------------------------------------
// Sheets: inspecting, the menu, grading, the end
// ------------------------------------------------------------------

let sheetLocked = false;
function openSheet(nodes, { locked = false } = {}) {
  const inner = $('sheet-inner');
  inner.textContent = '';
  inner.append(...nodes.filter(Boolean));
  $('sheet').hidden = false;
  $('scrim').hidden = false;
  sheetLocked = locked;
  $('sheet').scrollTop = 0;
}

function closeSheet() {
  $('sheet').hidden = true;
  $('scrim').hidden = true;
  sheetLocked = false;
}

function inspectUnit(i) {
  const s = setup.units[i], u = view.units[i];
  const facts = el('dl', { class: 'facts' }, [
    el('dt', {}, 'HP'), el('dd', {}, u.alive ? `${u.hp} of ${u.max}` : u.gone),
    el('dt', {}, 'Weight'), el('dd', {}, s.weight),
    el('dt', {}, 'Moves'), el('dd', {}, `${s.move} a turn`),
    s.side === 'enemy' ? el('dt', {}, 'Power') : null, s.side === 'enemy' ? el('dd', {}, `${s.power} (enemies act strongest first)`) : null,
    el('dt', {}, 'Defence'), el('dd', {}, s.defence),
    u.alive && u.on ? el('dt', {}, 'Standing at') : null, u.alive && u.on ? el('dd', {}, `(${u.x},${u.y}), height ${view.board.height[idx(u.x, u.y)]}`) : null,
    u.burrow ? el('dt', {}, 'Underground') : null, u.burrow ? el('dd', {}, `bursts out at (${u.burrow[0]},${u.burrow[1]})`) : null,
  ]);
  const kit = el('ul', { class: 'kit' }, s.kit.map((k) => el('li', {}, [el('span', {}, [el('span', { class: 'nm' }, k.name), el('span', { class: 'slot' }, SLOT_WORDS[k.slot] ?? k.slot)]), el('span', { class: 'tx' }, k.text)])));
  const showing = danger && danger.unit === i;
  openSheet([
    el('p', { class: 'eyebrow' }, s.side === 'party' ? 'Your party' : s.beast ? 'Enemy beast' : 'Enemy'),
    el('h2', {}, s.name),
    u.status ? el('p', { class: 'muted' }, u.status) : null,
    facts,
    s.beast ? el('p', { class: 'small muted' }, "It has no choices of its own: it acts straight after its handler, going for whatever it was last pointed at.") : null,
    el('h3', {}, 'Moves'),
    kit,
    el('div', { class: 'row' }, [
      s.side === 'enemy' && u.alive ? el('button', { class: 'ghost', type: 'button', onclick: () => { danger = showing ? null : markDanger(i); closeSheet(); render(); } }, showing ? 'Hide its danger zone' : 'Show its danger zone') : null,
      el('button', { class: 'ghost', type: 'button', onclick: closeSheet }, 'Close'),
    ]),
  ]);
}

function inspectTile(x, y) {
  const i = idx(x, y), b = view.board;
  const terrain = setup.terrain[b.terrain[i]];
  const lines = [];
  const height = b.height[i];
  lines.push(terrain.noGround ? 'No ground: anything that goes in falls and is lost.' : `${terrain.name}, height ${height} (${HEIGHT_WORDS[height] ?? height}).${terrain.walkable ? ` Costs ${terrain.cost} to enter.` : ' Nobody can walk here.'}`);
  if (b.feature[i] >= 0) {
    const f = setup.features[b.feature[i]];
    const parts = [];
    if (b.dead[i]) parts.push('dead');
    if (f.feeds) parts.push(`feeds ${f.feeds}`);
    if (f.uses) parts.push(`${b.uses[i]} of ${f.uses} uses`);
    if (f.hp) parts.push(`${b.fhp[i]} HP`);
    if (f.blocks) parts.push('blocks movement');
    if (f.extra) parts.push(`${f.extra} extra to enter`);
    if (f.creature) parts.push('moves on its own');
    lines.push(`${f.name}${parts.length ? `: ${parts.join(', ')}` : ''}.`);
  }
  if (b.trap[i] === 4) lines.push("A goblin's snap trap: it bites whoever steps on it.");
  else if (b.trap[i] > 0) lines.push('A root trap: it catches enemies who step on it.');
  if (b.rumble[i] >= 0) lines.push(`Rumbling ground: ${unitName(b.rumble[i])} bursts out here next turn.`);
  if (danger?.tiles.has(i)) lines.push(danger.unit < 0 ? 'An enemy could hit this tile on its next turn.' : `${unitName(danger.unit)} could hit this tile on its next turn.`);
  openSheet([
    el('p', { class: 'eyebrow' }, 'Tile'),
    el('h2', {}, `(${x},${y})`),
    ...lines.map((l) => el('p', {}, l)),
    el('div', { class: 'row' }, [el('button', { class: 'ghost', type: 'button', onclick: closeSheet }, 'Close')]),
  ]);
}

function askGrade(pending) {
  return new Promise((resolve) => {
    gradeAnswer = resolve;
    const attack = pending.need === 'attack';
    const table = attack ? boot.attackBonus : boot.guardReduction;
    const sub = (n) => (attack ? (n ? `+${n} damage` : 'no extra damage') : (n ? `${n} less damage` : 'takes the full hit'));
    const pick = (grade) => { closeSheet(); gradeAnswer = null; resolve(grade); };
    openSheet([
      el('p', { class: 'eyebrow' }, attack ? 'Action command' : 'Guard!'),
      el('h2', {}, pending.text),
      el('p', { class: 'muted' }, attack ? 'How did the action command go?' : `How did the ${unitName(pending.target)}'s guard go?`),
      el('div', { class: 'grades' }, [
        el('button', { type: 'button', onclick: () => pick('fumble') }, ['Fumble', el('span', {}, sub(table[0]))]),
        el('button', { type: 'button', onclick: () => pick('good') }, ['Good', el('span', {}, sub(table[1]))]),
        el('button', { type: 'button', class: 'perfect', onclick: () => pick('perfect') }, ['Perfect', el('span', {}, sub(table[2]))]),
      ]),
    ], { locked: true });
  });
}

function showEnd() {
  const party = setup.units.map((s, i) => ({ s, u: view.units[i] })).filter((x) => x.s.side === 'party');
  const enemies = setup.units.map((s, i) => ({ s, u: view.units[i] })).filter((x) => x.s.side === 'enemy');
  const left = (list) => list.filter((x) => x.u.alive).map((x) => `${x.s.name} ${x.u.hp}/${x.u.max}`).join(', ');
  openSheet([
    el('p', { class: 'eyebrow' }, `${NAMES[setup.encounter] ?? setup.encounter} · ${view.round} round${view.round === 1 ? '' : 's'} · seed ${setup.seed}`),
    el('h2', { class: 'outcome' }, OUTCOMES[view.outcome]),
    el('p', {}, `Your party: ${left(party) || 'nobody left standing'}.`),
    el('p', {}, `Enemies left: ${left(enemies) || 'none'}.`),
    saved === 'saved' ? el('p', { class: 'small muted' }, 'Saved here, so Claude can replay this battle.') : null,
    el('div', { class: 'row' }, [
      el('button', { class: 'primary', type: 'button', onclick: () => newBattle(setup.encounter, String(setup.seed), setup.timing) }, 'Fight it again'),
      el('button', { class: 'ghost', type: 'button', onclick: leave }, 'Pick another fight'),
    ]),
    el('div', { class: 'row' }, [
      copyButton(),
      el('button', { class: 'ghost', type: 'button', onclick: closeSheet }, 'Look at the board'),
    ]),
    el('div', { id: 'copy-slot' }),
  ]);
}

function copyButton() {
  return el('button', { class: 'ghost', type: 'button', onclick: () => copyLog() }, 'Copy the battle log');
}

function copyLog() {
  const text = api.Log();
  const slot = $('copy-slot');
  const fallback = () => {
    if (!slot) return;
    slot.textContent = '';
    const box = el('textarea', { class: 'copybox', readonly: true, id: 'log-text' }, text);
    slot.append(el('p', { class: 'small muted' }, "Copying isn't allowed here, so select it all and copy it yourself:"), box);
    box.focus();
    box.select();
  };
  try {
    navigator.clipboard.writeText(text).then(() => caption('Copied the battle log. Paste it to Claude to have it replayed.', 3500), fallback);
  } catch { fallback(); }
}

function showMenu() {
  const paceButtons = el('div', { class: 'seg' }, [['normal', 'Normal'], ['fast', 'Fast'], ['off', 'No animation']].map(([id, label]) =>
    el('button', { type: 'button', 'aria-pressed': String(prefs.pace === id), onclick: () => { prefs.pace = id; store('set', 'frpg.prefs', prefs); showMenu(); } }, label)));
  const heights = el('button', { type: 'button', class: 'ghost', 'aria-pressed': String(prefs.heights), onclick: () => { prefs.heights = !prefs.heights; store('set', 'frpg.prefs', prefs); render(); showMenu(); } }, prefs.heights ? 'On' : 'Off');
  let leaving = false;
  const leaveBtn = el('button', { class: 'ghost', type: 'button' }, 'Leave this battle');
  leaveBtn.addEventListener('click', () => {
    if (!leaving) { leaving = true; leaveBtn.textContent = 'Tap again to leave (you can carry on later)'; return; }
    leave();
  });
  openSheet([
    el('p', { class: 'eyebrow' }, `${NAMES[setup.encounter] ?? setup.encounter} · seed ${setup.seed} · timing: ${TIMING_LABELS[setup.timing] ?? setup.timing}`),
    el('h2', {}, 'Menu'),
    el('div', { class: 'toggle' }, [el('span', {}, 'Height numbers on the board'), heights]),
    el('div', {}, [el('div', { class: 'small muted' }, 'Enemy turns'), paceButtons]),
    el('details', {}, [
      el('summary', {}, 'How to play'),
      el('ul', { class: 'small' }, [
        el('li', {}, 'Lit tiles are where the acting unit can walk. Tap one to see the path, then tap it again (or Walk).'),
        el('li', {}, 'Tap a move, then a ringed target, then confirm. Greyed moves say why when tapped.'),
        el('li', {}, "The hero's and the Loper's turns run side by side: swap between them whenever you like, until one ends its turn."),
        el('li', {}, 'Danger marks every tile an enemy could hit next turn. Tap a unit or a tile to inspect it.'),
        el('li', {}, 'Heights: 1 low, 2 base, 3 high, 4 very high. A dark band is the face of a step down to the south.'),
        el('li', {}, 'With the whole party on the edge and no enemy within 3 tiles, you can flee.'),
      ]),
    ]),
    el('div', { class: 'row' }, [copyButton(), leaveBtn]),
    el('div', { id: 'copy-slot' }),
    el('p', { class: 'footnote' }, `Rules ${boot.rules} · content ${boot.content}${db ? ` · ${saved === 'saved' ? 'saved for Claude' : 'saves for Claude each round'}` : ''}`),
    el('div', { class: 'row' }, [el('button', { class: 'ghost', type: 'button', onclick: closeSheet }, 'Close')]),
  ]);
}

// ------------------------------------------------------------------
// Wiring
// ------------------------------------------------------------------

// Read-only handles for automated tests of the page.
window.battleTest = Object.freeze({ view: () => view, setup: () => setup, busy: () => busy, mode: () => ui.mode });

$('board').addEventListener('click', onTap);
$('menu-btn').addEventListener('click', () => { if (setup) showMenu(); });
$('danger-btn').addEventListener('click', () => {
  if (!setup) return;
  danger = danger ? null : markDanger(-1);
  render();
});
$('scrim').addEventListener('click', () => { if (!sheetLocked) closeSheet(); });
addEventListener('keydown', (e) => {
  if (e.key !== 'Escape') return;
  if (!$('sheet').hidden && !sheetLocked) closeSheet();
  else if (setup && ui.mode !== 'idle') { ui = freshUi(); render(); }
});

if (window.claude?.hot?.ready) window.claude.hot.ready(start);
else start(window.claude?.hot?.data ?? {});
