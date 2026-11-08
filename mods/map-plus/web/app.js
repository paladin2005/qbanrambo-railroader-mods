'use strict';

const API = '/map-plus/api/';
const STORE = 'qbanrambo.map-plus';
const STATE_MS = 250;
const ROUTES_MS = 1000;
const DETAILS_MS = 2000;
const TERRAIN_MS = 1000;
const MIN_SCALE = 0.004;
const ORDER_MERGE = 8;
const MAX_SCALE = 30;
const FONT = '-apple-system, "Segoe UI", Roboto, sans-serif';
const CLASS_COLORS = ['#FF5280', '#C773FF', '#598CFF', '#33CCEB', '#4DDB6B'];
const ASPECT_COLORS = ['#FF4038', '#FFD633', '#4DE666', '#FFD633', '#4DE666', '#FF8C26'];
const ASPECT_NAMES = ['Stop', 'Approach', 'Clear', 'Diverging approach', 'Diverging clear', 'Restricting'];
const ROLE_COLORS = ['#ECE5D5', '#6BE36B', '#59B4FF', '#C9C0AE'];
const ROLE_WIDTHS = [1, 1.45, 1, 0.8];
const ROLE_ORDER = [3, 0, 2, 1];
const MODES = [
  { name: 'Manual', color: '#A2977F' },
  { name: 'Road', color: '#7BD66B' },
  { name: 'Yard', color: '#F0A340' },
  { name: 'Waypoint', color: '#8CE6FF' }
];
const CASING = 'rgba(12, 10, 8, 0.9)';
const ROUTE_COLOR = '#8CE6FF';
const INK = '#16130E';
const RAILS_SCALE = 2.5;
const SWITCH_SCALE = 0.5;
const SIGNAL_SCALE = 0.06;
const TERRAIN_LEVELS = [
  { span: 2000, size: 400, scale: 0.1 },
  { span: 1000, size: 400, scale: 0.3 },
  { span: 500, size: 400, scale: 0.6 },
  { span: 250, size: 400, scale: 1.6 }
];
const OVERVIEW_MPP = 20;
const OVERVIEW_GRID = 40;
const OVERVIEW_STRIP = 128;
const TERRAIN_JOBS_PER_WORKER = 2;
const TERRAIN_WORKERS = Math.max(1, Math.min(3, (navigator.hardwareConcurrency || 4) - 2));
const TERRAIN_CHUNKS = 160;
const TRAIL_SECONDS = 10;
const RAIL_OFFSET = 0.72;
const CAR_WIDTH = 4.6;
const MIN_CAR_PIXELS = 7;
const SNAKE_SCALE = 1;
const MARKER_PIXELS = 26;
const PLACES_MS = 5000;
const PLACE_SCALE = 0.035;
const SMALL_PLACE_SCALE = 0.08;
const FUEL_SCALE = 0.15;
const COUNT_SCALE = 0.1;
const NAME_SCALE = 0.35;
const MAP_LABEL_SCALE = 0.1;
const COMING_COLOR = '#F1B827';
const RIDER_COLOR = '#8CC8FF';
const FILTERS = [
  { key: 'mine', label: 'Mines' },
  { key: 'logging', label: 'Logging' },
  { key: 'mill', label: 'Mills' },
  { key: 'freight', label: 'Freight customers' },
  { key: 'oil', label: 'Fuel dealers' },
  { key: 'mow', label: 'Track materials' },
  { key: 'interchange', label: 'Interchanges' },
  { key: 'station', label: 'Passenger stations' },
  { key: 'engine', label: 'Engine terminals' },
  { key: 'fuel', label: 'Fuel stops', color: '#2F7FCB' }
];
const SEGMENTS_MS = 30000;
const MAX_SPEEDS = [10, 25, 40, 60, 80, 120];
const PLACE_KINDS = { station: 'Passenger station', interchange: 'Interchange', industry: 'Industry track' };
const SPOTTED_COLOR = '#7BE07B';
const CATEGORIES = {
  mine: { label: 'Mine', color: '#66717F' },
  logging: { label: 'Logging', color: '#4C9A52' },
  mill: { label: 'Mill', color: '#C0773A' },
  freight: { label: 'Freight customer', color: '#A08A5E' },
  oil: { label: 'Fuel dealer', color: '#7A5230' },
  mow: { label: 'Track materials', color: '#C49A22' },
  interchange: { label: 'Interchange', color: '#8A63C9' },
  station: { label: 'Passenger station', color: '#3C86D0' },
  engine: { label: 'Engine terminal', color: '#B14B3A' },
  water: { label: 'Water', color: '#2F7FCB' },
  coal: { label: 'Coal', color: '#3A3631' },
  diesel: { label: 'Diesel', color: '#C79A1E' }
};

const canvas = document.getElementById('map');
const ctx = canvas.getContext('2d');
const backdrop = document.createElement('canvas');
const bctx = backdrop.getContext('2d');
const tooltip = document.getElementById('tooltip');
const notice = document.getElementById('notice');
const menu = document.getElementById('menu');
const toast = document.getElementById('toast');
const legend = document.getElementById('legend');
const searchInput = document.getElementById('search-input');
const searchResults = document.getElementById('search-results');
const layersCard = document.getElementById('layers');
const scaleText = document.getElementById('scale-text');
const scaleBar = document.getElementById('scale-bar');
const terrainText = document.getElementById('terrain-text');

const syncButton = document.getElementById('sync');
const LINKED = '<path d="M9.5 14.5l5-5"/><path d="M11 6.5l1.5-1.5a3.5 3.5 0 0 1 5 5L16 11.5"/><path d="M13 17.5L11.5 19a3.5 3.5 0 0 1-5-5L8 12.5"/>';
const UNLINKED = '<path d="M11 6.5l1.5-1.5a3.5 3.5 0 0 1 5 5L16 11.5"/><path d="M13 17.5L11.5 19a3.5 3.5 0 0 1-5-5L8 12.5"/><path d="M4 9.5h2.5M9.5 4v2.5M20 14.5h-2.5M14.5 20v-2.5"/>';

const saved = load();
const view = { x: saved.x ?? 0, z: saved.z ?? 0, scale: saved.scale ?? 0.1 };
const layers = Object.assign({ terrain: true, classes: false, condition: false, industry: true, mainline: true, names: true, limits: false, places: true, route: true, destinations: true, orders: true, signals: true, fusees: true, cars: true, labels: true, alerts: true, hiddenKinds: [] }, saved.layers || {});
let fitted = saved.scale !== undefined;
let selected = saved.selected || null;
let selectedIndustry = null;
let gameSelected;
let selectedFuel = null;
let industryPath = { key: '', path: null };
let segmentInfo = null;
let panelInfo = null;
let orderHits = [];
let highlight = null;
let highlightPath = { key: '', path: null };
let highlightArrow = null;
let conditionPaths = { key: null, degraded: null, severe: null };
let destinationPaths = { key: '', coming: null, spotted: null };
let hoverSegment = -1;
let pageVersion = null;
let gamePaths = null;
let follow = !!saved.follow;
let sync = saved.sync !== false;

let track = null;
let segments = [];
let switches = [];
let paths = null;
let trackRevision = 0;
let trackLoading = false;
let switchState = '';
let prev = new Map();
let curr = new Map();
let currAt = 0;
let interval = STATE_MS;
let locos = [];
let locoById = new Map();
let groups = new Map();
let signals = [];
let fusees = [];
let toastTimer = 0;
let routes = new Map();
let details = { cars: {}, signals: {} };
let drag = null;
let width = 0;
let height = 0;
let dpr = 1;
let flight = null;
let listOpen = false;
let ping = null;
let search = { items: [], active: 0 };
let scaleKey = '';
let backdropKey = '';
let tagRects = [];
let places = { industries: [], fuel: [] };
let placeHits = [];
const heading = new Map();
const trails = new Map();

const terrain = {
  key: null,
  worker: null,
  workers: [],
  busy: [],
  rect: null,
  overview: null,
  strips: 0,
  stripsDone: 0,
  chunks: new Map(),
  queue: [],
  inflight: new Map(),
  revision: 0,
  generation: 0,
  nextId: 1
};

function load() {
  try {
    return JSON.parse(sessionStorage.getItem(STORE) || localStorage.getItem(STORE) || '{}');
  } catch (error) {
    return {};
  }
}

function save() {
  const state = { x: view.x, z: view.z, scale: view.scale, layers, selected, follow };
  localStorage.setItem(STORE, JSON.stringify(state));
  sessionStorage.setItem(STORE, JSON.stringify({ ...state, sync }));
}

function resize() {
  dpr = window.devicePixelRatio || 1;
  width = window.innerWidth;
  height = window.innerHeight;
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);
  backdrop.width = canvas.width;
  backdrop.height = canvas.height;
  backdropKey = '';
}

const sx = x => (x - view.x) * view.scale + width / 2;
const sy = z => height / 2 - (z - view.z) * view.scale;
const wx = px => (px - width / 2) / view.scale + view.x;
const wz = py => view.z - (py - height / 2) / view.scale;

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function rgba(hex, alpha) {
  const value = parseInt(hex.slice(1), 16);
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`;
}

function roundRect(g, x, y, w, h, r) {
  const radius = Math.max(0, Math.min(r, w / 2, h / 2));
  g.moveTo(x + radius, y);
  g.lineTo(x + w - radius, y);
  g.arcTo(x + w, y, x + w, y + radius, radius);
  g.lineTo(x + w, y + h - radius);
  g.arcTo(x + w, y + h, x + w - radius, y + h, radius);
  g.lineTo(x + radius, y + h);
  g.arcTo(x, y + h, x, y + h - radius, radius);
  g.lineTo(x, y + radius);
  g.arcTo(x, y, x + radius, y, radius);
  g.closePath();
}

function setStatus(live, text) {
  document.getElementById('status').classList.toggle('live', live);
  document.getElementById('status-text').textContent = text;
}

function showNotice(text) {
  notice.textContent = text;
  notice.classList.toggle('hidden', !text);
}

async function fetchJson(name) {
  const response = await fetch(API + name, { cache: 'no-store' });
  return response.json();
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function loop(name, ms, handle) {
  for (;;) {
    try {
      handle(await fetchJson(name));
    } catch (error) {
      if (name === 'state') {
        setStatus(false, 'No connection');
        showNotice("Can't reach the game. Is Railroader running with Preferences > Features > Experimental > HTTP Server on?");
      }
    }
    await sleep(ms);
  }
}

async function loadTrack() {
  if (trackLoading) return;
  trackLoading = true;
  try {
    const data = await fetchJson('track');
    if (data.ok) {
      track = data;
      buildTrack();
      if (!fitted) fit();
    }
  } finally {
    trackLoading = false;
  }
}

function bezierPoint(p, t) {
  const u = 1 - t;
  const a = u * u * u;
  const b = 3 * u * u * t;
  const c = 3 * u * t * t;
  const d = t * t * t;
  return [a * p[0] + b * p[2] + c * p[4] + d * p[6], a * p[1] + b * p[3] + c * p[5] + d * p[7]];
}

function subCurve(p, end, t) {
  const s = end === 0 ? t : 1 - t;
  const lerp = (a, b) => a + (b - a) * s;
  const x01 = lerp(p[0], p[2]), z01 = lerp(p[1], p[3]);
  const x12 = lerp(p[2], p[4]), z12 = lerp(p[3], p[5]);
  const x23 = lerp(p[4], p[6]), z23 = lerp(p[5], p[7]);
  const x012 = lerp(x01, x12), z012 = lerp(z01, z12);
  const x123 = lerp(x12, x23), z123 = lerp(z12, z23);
  const x = lerp(x012, x123), z = lerp(z012, z123);
  return end === 0 ? [p[0], p[1], x01, z01, x012, z012, x, z] : [x, z, x123, z123, x23, z23, p[6], p[7]];
}

function curveTo(path, p) {
  path.moveTo(p[0], p[1]);
  path.bezierCurveTo(p[2], p[3], p[4], p[5], p[6], p[7]);
}

function addPath(map, key, base, segment) {
  let entry = map.get(key);
  if (!entry) {
    entry = Object.assign({ path: new Path2D() }, base);
    map.set(key, entry);
  }
  curveTo(entry.path, segment.p);
}

function buildTrack() {
  segments = track.segments.map(raw => {
    const p = raw.slice(0, 8);
    const flags = raw[11] || 0;
    const kind = raw[9];
    const role = kind === 1 ? 1 : kind === 2 ? 2 : (flags & 4) ? 3 : 0;
    const style = flags & 2 ? 'tunnel' : flags & 1 ? (flags & 8 ? 'steel' : 'bridge') : 'normal';
    const xs = [p[0], p[2], p[4], p[6]];
    const zs = [p[1], p[3], p[5], p[7]];
    const chord = Math.hypot(p[6] - p[0], p[7] - p[1]);
    const net = Math.hypot(p[2] - p[0], p[3] - p[1]) + Math.hypot(p[4] - p[2], p[5] - p[3]) + Math.hypot(p[6] - p[4], p[7] - p[5]);
    return {
      p, state: raw[8], kind, cls: raw[10], flags, role, style,
      minX: Math.min(...xs), maxX: Math.max(...xs), minZ: Math.min(...zs), maxZ: Math.max(...zs),
      length: Math.max(0.1, (chord + net) / 2), samples: null
    };
  });
  paths = { unavailable: new Path2D(), unbuilt: new Path2D(), kinds: new Map(), classes: new Map() };
  for (const segment of segments) {
    if (segment.state === 1) {
      curveTo(paths.unavailable, segment.p);
      continue;
    }
    if (segment.state === 2) {
      curveTo(paths.unbuilt, segment.p);
      continue;
    }
    addPath(paths.kinds, `${segment.style}|${segment.role}`, { style: segment.style, role: segment.role, color: ROLE_COLORS[segment.role] }, segment);
    if (segment.cls > 0) {
      addPath(paths.classes, `${segment.style}|${segment.role}|${segment.cls}`, { style: segment.style, role: segment.role, color: CLASS_COLORS[segment.cls - 1] }, segment);
    }
  }
  switches = track.switches || [];
  trackRevision++;
  sendTrackToTerrain();
}

function colorOf(segment) {
  if (layers.classes) return segment.cls > 0 ? CLASS_COLORS[segment.cls - 1] : ROLE_COLORS[0];
  return roleHidden(segment.role) ? ROLE_COLORS[0] : ROLE_COLORS[segment.role];
}

function roleHidden(role) {
  return (role === 1 && !layers.mainline) || (role === 2 && !layers.industry);
}

function kindColor(entry) {
  return !layers.classes && roleHidden(entry.role) ? ROLE_COLORS[0] : entry.color;
}

function showCar(car) {
  return layers.cars || (car && car[8] === 1);
}

function entries(map, style) {
  return [...map.values()].filter(entry => entry.style === style).sort((a, b) => ROLE_ORDER.indexOf(a.role) - ROLE_ORDER.indexOf(b.role));
}

function fitTarget() {
  if (!track || !track.bounds) return null;
  const [minX, minZ, maxX, maxZ] = track.bounds;
  return {
    x: (minX + maxX) / 2,
    z: (minZ + maxZ) / 2,
    scale: clamp(Math.min(width / ((maxX - minX) * 1.1 || 1), (height - 40) / ((maxZ - minZ) * 1.1 || 1)), MIN_SCALE, MAX_SCALE)
  };
}

function fit() {
  const target = fitTarget();
  if (!target) return;
  view.x = target.x;
  view.z = target.z;
  view.scale = target.scale;
  fitted = true;
}

function handleState(data) {
  if (!data.ok) {
    setStatus(true, 'Connected');
    showNotice(data.reason || 'Load a game to see the map');
    curr = new Map();
    prev = new Map();
    return;
  }
  setStatus(true, 'Live');
  showNotice('');
  if (data.pv) {
    if (pageVersion === null) pageVersion = data.pv;
    else if (data.pv !== pageVersion) {
      save();
      location.reload();
      return;
    }
  }
  if (!track || track.version !== data.version) loadTrack();
  const next = new Map();
  for (const car of data.cars) next.set(car[0], car);
  const now = performance.now();
  if (currAt) interval = clamp(now - currAt, 100, 1000);
  prev = curr;
  curr = next;
  currAt = now;
  locos = data.locos;
  locoById = new Map(locos.map(loco => [loco.i, loco]));
  signals = data.signals;
  fusees = data.fl || [];
  highlight = data.hl || null;
  gamePaths = data.gp || null;
  setOrdersSupport(typeof data.to === 'number' ? data.to : data.to ? 2 : 0);
  switchState = track && data.sv === track.version ? data.sw || '' : '';
  updateMotion(now / 1000);
  followGameSelection(data.sel || null);
}

function followGameSelection(id) {
  if (id === gameSelected) return;
  gameSelected = id;
  if (sync && id && id !== selected && locoById.has(id)) select(id, false);
}

function selectInGame(id) {
  gameSelected = id;
  fetch(API + 'select', { cache: 'no-store', headers: { 'X-Map-Plus-Train': id } }).catch(() => {});
}

function center(car) {
  return { x: (car[1] + car[3]) / 2, z: (car[2] + car[4]) / 2 };
}

function selectedGroup() {
  const car = selected ? curr.get(selected) : null;
  return car && car[12] ? car[12] : selected;
}

function updateMotion(now) {
  groups = new Map();
  for (const car of curr.values()) {
    const group = car[12];
    if (!group) continue;
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push(car);
  }
  for (const list of groups.values()) list.sort((a, b) => (a[13] || 0) - (b[13] || 0));
  for (const car of curr.values()) {
    if (car[8] !== 1) continue;
    const before = prev.get(car[0]);
    if (!before) continue;
    const a = center(car);
    const b = center(before);
    const dx = a.x - b.x;
    const dz = a.z - b.z;
    if (Math.hypot(dx, dz) < 0.02) continue;
    heading.set(car[0], dx * (car[1] - car[3]) + dz * (car[2] - car[4]) >= 0 ? 1 : -1);
  }
  for (const [id, list] of groups) {
    const lead = curr.get(id);
    const before = prev.get(id);
    const loco = locoById.get(id);
    if (!lead || !before || !loco || loco.s < 1) continue;
    const a = center(lead);
    const b = center(before);
    const length = Math.hypot(a.x - b.x, a.z - b.z);
    if (length < 0.05) continue;
    const ux = (a.x - b.x) / length;
    const uz = (a.z - b.z) / length;
    const first = list[0];
    const last = list[list.length - 1];
    const along = car => center(car).x * ux + center(car).z * uz;
    const tail = along(first) <= along(last) ? first : last;
    let trail = trails.get(id);
    if (!trail || trail.car !== tail[0]) {
      trail = { car: tail[0], points: [] };
      trails.set(id, trail);
    }
    trail.ux = ux;
    trail.uz = uz;
    trail.length = tail[5];
    const c = center(tail);
    const point = { x: c.x - ux * tail[5] / 2, z: c.z - uz * tail[5] / 2, t: now };
    const latest = trail.points[trail.points.length - 1];
    if (!latest || Math.hypot(point.x - latest.x, point.z - latest.z) > 0.5) trail.points.push(point);
  }
  for (const [id, trail] of trails) {
    trail.points = trail.points.filter(point => now - point.t < TRAIL_SECONDS);
    if (!trail.points.length || !curr.has(trail.car)) trails.delete(id);
  }
}

function handleRoutes(data) {
  if (!data.ok) return;
  routes = new Map(data.routes.map(route => [route.i, route.p]));
}

function handleDetails(data) {
  if (data.ok) details = data;
}

function routeFor(id) {
  if (!id) return null;
  if (routes.has(id)) return routes.get(id);
  const group = selectedGroup();
  for (const car of groups.get(group) || []) {
    if (car[8] === 1 && routes.has(car[0])) return routes.get(car[0]);
  }
  return null;
}

async function terrainLoop() {
  for (;;) {
    try {
      const status = await fetchJson('terrain');
      if (status.ok && status.state === 'ready' && status.key !== terrain.key) {
        terrainText.textContent = 'Loading terrain';
        const response = await fetch(API + 'terrain-data', { cache: 'no-store' });
        if (response.ok) {
          startTerrain(await response.arrayBuffer());
          terrain.key = status.key;
        }
      } else if (status.ok && status.state === 'building') {
        terrainText.textContent = `Reading terrain ${Math.round(100 * status.done / Math.max(1, status.total))}%`;
      } else if (!terrain.worker) {
        terrainText.textContent = '';
      }
    } catch (error) {
      if (!terrain.worker) terrainText.textContent = '';
    }
    await sleep(terrain.key ? 15000 : TERRAIN_MS);
  }
}

function startTerrain(buffer) {
  for (const worker of terrain.workers) worker.terminate();
  terrain.workers = [];
  terrain.busy = [];
  for (let index = 0; index < TERRAIN_WORKERS; index++) {
    const worker = new Worker('/map-plus/terrain.js');
    worker.onmessage = event => onTerrainMessage(event, index);
    terrain.workers.push(worker);
    terrain.busy.push(0);
  }
  terrain.worker = terrain.workers[0];
  terrain.rect = null;
  terrain.overview = null;
  terrain.chunks = new Map();
  terrain.queue = [];
  terrain.inflight = new Map();
  terrain.revision++;
  terrainText.textContent = 'Painting terrain';
  terrain.workers.forEach((worker, index) => {
    const copy = index === terrain.workers.length - 1 ? buffer : buffer.slice(0);
    worker.postMessage({ type: 'load', buffer: copy }, [copy]);
  });
  sendTrackToTerrain();
}

function sendTrackToTerrain() {
  if (!terrain.worker || !track) return;
  const built = track.segments.filter(raw => raw[8] === 0 && !((raw[11] || 0) & 2)).map(raw => raw.slice(0, 8));
  for (const worker of terrain.workers) {
    worker.postMessage({ type: 'rivers', rivers: track.rivers || [] });
    worker.postMessage({ type: 'track', segments: built });
  }
  if (!terrain.rect) return;
  terrain.chunks = new Map();
  terrain.generation++;
  queueOverview();
  terrain.revision++;
  pumpTerrain();
}

function queueOverview() {
  const rect = terrain.rect;
  const surface = terrain.overview.canvas;
  terrain.strips = Math.ceil(surface.height / OVERVIEW_STRIP);
  terrain.stripsDone = 0;
  terrain.queue = terrain.queue.filter(job => job.kind !== 'overview');
  for (let row = 0; row < surface.height; row += OVERVIEW_STRIP) {
    terrain.queue.push({ kind: 'overview', generation: terrain.generation, row, x0: rect.x0, z0: rect.z0 + row * OVERVIEW_MPP, mpp: OVERVIEW_MPP, grid: OVERVIEW_GRID, width: surface.width, height: Math.min(OVERVIEW_STRIP, surface.height - row) });
  }
}

function onTerrainMessage(event, index) {
  const message = event.data;
  if (message.type === 'loaded') {
    if (terrain.rect) return;
    const rect = message.rect;
    terrain.rect = rect;
    const surface = document.createElement('canvas');
    surface.width = Math.ceil(rect.w / OVERVIEW_MPP);
    surface.height = Math.ceil(rect.h / OVERVIEW_MPP);
    terrain.overview = { canvas: surface, context: surface.getContext('2d') };
    queueOverview();
    terrain.revision++;
    pumpTerrain();
    return;
  }
  const job = terrain.inflight.get(message.id);
  terrain.inflight.delete(message.id);
  if (job && terrain.busy[index] > 0) terrain.busy[index]--;
  if (message.type === 'error') console.warn('Map Plus terrain:', message.message);
  if (message.type === 'pixels' && job && job.generation !== terrain.generation) terrain.revision++;
  if (message.type === 'pixels' && job && job.generation === terrain.generation) {
    const image = new ImageData(new Uint8ClampedArray(message.pixels), message.width, message.height);
    if (job.kind === 'overview') {
      terrain.overview.context.putImageData(image, 0, job.row);
      terrain.stripsDone++;
      terrainText.textContent = terrain.stripsDone < terrain.strips ? `Painting terrain ${Math.round(100 * terrain.stripsDone / terrain.strips)}%` : '';
    } else {
      const surface = document.createElement('canvas');
      surface.width = message.width;
      surface.height = message.height;
      surface.getContext('2d').putImageData(image, 0, 0);
      terrain.chunks.set(job.key, { canvas: surface, used: performance.now() });
      evictTerrain();
    }
    terrain.revision++;
  }
  pumpTerrain();
}

function evictTerrain() {
  if (terrain.chunks.size <= TERRAIN_CHUNKS) return;
  const entries = [...terrain.chunks.entries()].sort((a, b) => a[1].used - b[1].used);
  for (const [key] of entries.slice(0, terrain.chunks.size - TERRAIN_CHUNKS)) terrain.chunks.delete(key);
}

function pumpTerrain() {
  if (!terrain.workers.length) return;
  while (terrain.queue.length) {
    let index = 0;
    for (let other = 1; other < terrain.busy.length; other++) {
      if (terrain.busy[other] < terrain.busy[index]) index = other;
    }
    if (terrain.busy[index] >= TERRAIN_JOBS_PER_WORKER) break;
    const job = terrain.queue.shift();
    const id = terrain.nextId++;
    terrain.inflight.set(id, job);
    terrain.busy[index]++;
    terrain.workers[index].postMessage({ type: 'render', id, x0: job.x0, z0: job.z0, mpp: job.mpp, grid: job.grid, width: job.width, height: job.height });
  }
}

function terrainLevel() {
  let level = -1;
  TERRAIN_LEVELS.forEach((entry, index) => {
    if (view.scale >= entry.scale) level = index;
  });
  return level;
}

function visibleChunks(level) {
  const rect = terrain.rect;
  const { span } = TERRAIN_LEVELS[level];
  const box = visibleBox(0);
  const minX = Math.max(box.minX, rect.x0);
  const maxX = Math.min(box.maxX, rect.x0 + rect.w);
  const minZ = Math.max(box.minZ, rect.z0);
  const maxZ = Math.min(box.maxZ, rect.z0 + rect.h);
  const chunks = [];
  if (minX >= maxX || minZ >= maxZ) return chunks;
  for (let j = Math.floor((minZ - rect.z0) / span); j <= Math.floor((maxZ - rect.z0 - 0.01) / span); j++) {
    for (let i = Math.floor((minX - rect.x0) / span); i <= Math.floor((maxX - rect.x0 - 0.01) / span); i++) {
      chunks.push({ key: `${level}:${i}:${j}`, x: rect.x0 + i * span, z: rect.z0 + j * span, span });
    }
  }
  return chunks;
}

function requestTerrain(level) {
  const wanted = new Set();
  const jobs = [];
  if (level >= 0) {
    const { size } = TERRAIN_LEVELS[level];
    for (const chunk of visibleChunks(level)) {
      wanted.add(chunk.key);
      if (terrain.chunks.has(chunk.key)) continue;
      if ([...terrain.inflight.values()].some(job => job.key === chunk.key)) continue;
      const distance = Math.hypot(chunk.x + chunk.span / 2 - view.x, chunk.z + chunk.span / 2 - view.z);
      jobs.push({ kind: 'chunk', generation: terrain.generation, key: chunk.key, x0: chunk.x, z0: chunk.z, mpp: chunk.span / size, width: size, height: size, distance });
    }
  }
  jobs.sort((a, b) => a.distance - b.distance);
  const strips = terrain.queue.filter(job => job.kind === 'overview');
  terrain.queue = [...jobs, ...strips];
  pumpTerrain();
}

function locoName(id) {
  const loco = locoById.get(id);
  return loco ? loco.n : '';
}

function escapeHtml(text) {
  return String(text).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
}

function select(id, inGame = true) {
  if (id) {
    selectedIndustry = null;
    selectedFuel = null;
    if (inGame && sync && id !== gameSelected) selectInGame(id);
  }
  selected = id;
  save();
  refreshPanel();
}

function mix() {
  return clamp((performance.now() - currAt) / interval, 0, 1);
}

function carPose(id, t) {
  const now = curr.get(id);
  const before = prev.get(id) || now;
  const lerp = (a, b) => a + (b - a) * t;
  return { car: now, fx: lerp(before[1], now[1]), fz: lerp(before[2], now[2]), rx: lerp(before[3], now[3]), rz: lerp(before[4], now[4]) };
}

function carShape(pose) {
  const car = pose.car;
  const cx = (pose.fx + pose.rx) / 2;
  const cz = (pose.fz + pose.rz) / 2;
  let ux = pose.fx - pose.rx;
  let uz = pose.fz - pose.rz;
  const length = Math.hypot(ux, uz);
  if (length < 0.01) {
    ux = 1;
    uz = 0;
  } else {
    ux /= length;
    uz /= length;
  }
  const wide = Math.max(MIN_CAR_PIXELS, CAR_WIDTH * view.scale);
  const long = view.scale < SNAKE_SCALE ? car[5] * view.scale : Math.max((car[5] - 0.9) * view.scale, wide * 1.3);
  return { x: sx(cx), y: sy(cz), ux, uy: -uz, angle: Math.atan2(-uz, ux), long, wide };
}

function frame(time) {
  stepFlight();
  const t = mix();
  if (follow && selected && curr.has(selected)) {
    const pose = carPose(selected, t);
    const dx = (pose.fx + pose.rx) / 2 - view.x;
    const dz = (pose.fz + pose.rz) / 2 - view.z;
    if (Math.hypot(dx, dz) * view.scale > 0.2) {
      view.x += dx * 0.2;
      view.z += dz * 0.2;
    }
  }
  const key = [view.x, view.z, view.scale, width, height, dpr, layers.terrain, layers.classes, layers.industry, layers.mainline, layers.names, layers.limits, terrain.revision, trackRevision, switchState].join('|');
  if (key !== backdropKey) {
    backdropKey = key;
    drawBackdrop();
  }
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.drawImage(backdrop, 0, 0);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const group = selectedGroup();
  const chosen = highlights(group);
  drawCondition(ctx);
  drawDestinations(ctx);
  drawRoute(ctx, time);
  if (layers.signals) drawSignals(ctx);
  if (layers.fusees) drawFusees(ctx, time);
  drawIndustryTracks(ctx);
  drawHighlight(ctx, time);
  drawHoverTrack(ctx);
  drawPlaces(ctx);
  if (track) drawTowns(ctx);
  drawTrails(ctx, t, time / 1000, group);
  drawHeadlights(ctx, t);
  drawCars(ctx, t, time, chosen);
  drawGamePaths(ctx);
  drawGhost(ctx);
  drawOrders(ctx);
  drawTags(ctx, t, group, chosen);
  drawHighlightArrow(ctx);
  updateLegend();
  updateScale();
  requestAnimationFrame(frame);
}

function worldTransform(g) {
  g.setTransform(dpr * view.scale, 0, 0, -dpr * view.scale, dpr * (width / 2 - view.scale * view.x), dpr * (height / 2 + view.scale * view.z));
}

function drawBackdrop() {
  const g = bctx;
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.fillStyle = INK;
  g.fillRect(0, 0, width, height);
  if (layers.terrain) drawTerrain(g);
  if (paths) {
    if (view.scale >= RAILS_SCALE) drawRails(g);
    else drawTrackLines(g);
    drawSwitches(g);
  }
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  if (track) drawMapLabels(g, townBoxes(g));
}

function visibleBox(margin) {
  return { minX: wx(-margin), maxX: wx(width + margin), minZ: wz(height + margin), maxZ: wz(-margin) };
}

function drawTerrain(g) {
  const rect = terrain.rect;
  if (!rect) return;
  worldTransform(g);
  g.imageSmoothingEnabled = true;
  const level = terrainLevel();
  const pad = 1 / view.scale;
  const now = performance.now();
  const current = level >= 0 ? visibleChunks(level) : [];
  const complete = level >= 0 && current.every(chunk => terrain.chunks.has(chunk.key));
  if (!complete) {
    g.imageSmoothingQuality = 'medium';
    if (terrain.overview) g.drawImage(terrain.overview.canvas, rect.x0, rect.z0, rect.w, rect.h);
    g.imageSmoothingQuality = 'low';
    for (let index = 0; index < level; index++) {
      for (const chunk of visibleChunks(index)) {
        const entry = terrain.chunks.get(chunk.key);
        if (!entry) continue;
        entry.used = now;
        g.drawImage(entry.canvas, chunk.x, chunk.z, chunk.span + pad, chunk.span + pad);
      }
    }
  }
  g.imageSmoothingQuality = 'low';
  for (const chunk of current) {
    const entry = terrain.chunks.get(chunk.key);
    if (!entry) continue;
    entry.used = now;
    g.drawImage(entry.canvas, chunk.x, chunk.z, chunk.span + pad, chunk.span + pad);
  }
  requestTerrain(level);
}

function strokeWorld(g, path, color, pixels, dash) {
  g.strokeStyle = color;
  g.lineWidth = pixels / view.scale;
  g.setLineDash(dash ? dash.map(value => value / view.scale) : []);
  g.stroke(path);
}

function trackWidth() {
  return clamp(1.2 + view.scale * 2.4, 1.2, 5.5);
}

function drawTrackLines(g) {
  worldTransform(g);
  g.lineJoin = 'round';
  g.lineCap = 'round';
  const base = trackWidth();
  const cores = layers.classes ? paths.classes : paths.kinds;
  strokeWorld(g, paths.unavailable, 'rgba(110, 104, 92, 0.6)', base * 0.6);
  strokeWorld(g, paths.unbuilt, 'rgba(237, 231, 218, 0.45)', base * 0.6, [6, 5]);
  g.lineCap = 'butt';
  for (const entry of entries(cores, 'tunnel')) strokeWorld(g, entry.path, rgba(kindColor(entry), 0.6), base * ROLE_WIDTHS[entry.role], [7, 5]);
  g.lineCap = 'round';
  for (const entry of entries(paths.kinds, 'normal')) strokeWorld(g, entry.path, CASING, base * ROLE_WIDTHS[entry.role] + 2.4);
  for (const entry of entries(cores, 'normal')) strokeWorld(g, entry.path, kindColor(entry), base * ROLE_WIDTHS[entry.role]);
  g.lineCap = 'butt';
  const offset = Math.max(2.5, 3 / view.scale);
  for (const [style, deck] of [['bridge', '#C9A97A'], ['steel', '#B9C0C7']]) {
    const bridges = entries(paths.kinds, style);
    if (!bridges.length) continue;
    g.save();
    g.translate(offset, -offset);
    for (const entry of bridges) strokeWorld(g, entry.path, 'rgba(0, 0, 0, 0.35)', base * ROLE_WIDTHS[entry.role] + 7);
    g.restore();
    for (const entry of bridges) strokeWorld(g, entry.path, CASING, base * ROLE_WIDTHS[entry.role] + 7);
    for (const entry of bridges) strokeWorld(g, entry.path, deck, base * ROLE_WIDTHS[entry.role] + 4.6);
    for (const entry of bridges) strokeWorld(g, entry.path, CASING, base * ROLE_WIDTHS[entry.role] + 1.6);
    for (const entry of entries(cores, style)) strokeWorld(g, entry.path, kindColor(entry), base * ROLE_WIDTHS[entry.role]);
  }
  g.setLineDash([]);
  g.lineCap = 'round';
}

function samplesOf(segment) {
  if (segment.samples) return segment.samples;
  const n = Math.max(2, Math.ceil(segment.length / 1.5));
  const xs = new Float64Array(n + 1);
  const zs = new Float64Array(n + 1);
  const ds = new Float64Array(n + 1);
  for (let i = 0; i <= n; i++) {
    const [x, z] = bezierPoint(segment.p, i / n);
    xs[i] = x;
    zs[i] = z;
    if (i > 0) ds[i] = ds[i - 1] + Math.hypot(x - xs[i - 1], z - zs[i - 1]);
  }
  segment.samples = { xs, zs, ds, n };
  return segment.samples;
}

function samplePath(path, samples) {
  path.moveTo(samples.xs[0], samples.zs[0]);
  for (let i = 1; i <= samples.n; i++) path.lineTo(samples.xs[i], samples.zs[i]);
}

function addTies(path, samples, spacing = 0.6, half = 1.3, start = 0.3) {
  const { xs, zs, ds, n } = samples;
  let i = 1;
  for (let d = start; d < ds[n]; d += spacing) {
    while (i < n && ds[i] < d) i++;
    const piece = ds[i] - ds[i - 1] || 1;
    const f = (d - ds[i - 1]) / piece;
    const x = xs[i - 1] + (xs[i] - xs[i - 1]) * f;
    const z = zs[i - 1] + (zs[i] - zs[i - 1]) * f;
    const nx = -(zs[i] - zs[i - 1]) / piece;
    const nz = (xs[i] - xs[i - 1]) / piece;
    path.moveTo(x - nx * half, z - nz * half);
    path.lineTo(x + nx * half, z + nz * half);
  }
}

function addRails(path, samples, offset = RAIL_OFFSET) {
  const { xs, zs, n } = samples;
  for (const side of [-1, 1]) {
    for (let i = 0; i <= n; i++) {
      const a = Math.max(0, i - 1);
      const b = Math.min(n, i + 1);
      const dx = xs[b] - xs[a];
      const dz = zs[b] - zs[a];
      const length = Math.hypot(dx, dz) || 1;
      const x = xs[i] - dz / length * offset * side;
      const z = zs[i] + dx / length * offset * side;
      if (i === 0) path.moveTo(x, z);
      else path.lineTo(x, z);
    }
  }
}

function drawRails(g) {
  worldTransform(g);
  g.setLineDash([]);
  g.lineJoin = 'round';
  g.lineCap = 'butt';
  const base = trackWidth();
  strokeWorld(g, paths.unavailable, 'rgba(110, 104, 92, 0.6)', base * 0.6);
  strokeWorld(g, paths.unbuilt, 'rgba(237, 231, 218, 0.45)', base * 0.6, [6, 5]);
  const box = visibleBox(20);
  const edges = new Map();
  const ballast = new Path2D();
  const ties = new Path2D();
  const rails = new Path2D();
  const tunnels = new Path2D();
  const shadows = new Path2D();
  const bents = new Path2D();
  const woodDecks = new Path2D();
  const steelDecks = new Path2D();
  const woodSides = new Path2D();
  const steelSides = new Path2D();
  const bridgeTies = new Path2D();
  const bridgeRails = new Path2D();
  for (const segment of segments) {
    if (segment.state !== 0) continue;
    if (segment.minX > box.maxX || segment.maxX < box.minX || segment.minZ > box.maxZ || segment.maxZ < box.minZ) continue;
    const samples = samplesOf(segment);
    if (segment.style === 'tunnel') {
      samplePath(tunnels, samples);
      continue;
    }
    if (segment.style === 'bridge' || segment.style === 'steel') {
      const steel = segment.style === 'steel';
      samplePath(shadows, samples);
      samplePath(steel ? steelDecks : woodDecks, samples);
      if (!steel) addTies(bents, samples, 4.6, 3, 1.5);
      addRails(steel ? steelSides : woodSides, samples, 2.45);
      addTies(bridgeTies, samples);
      addRails(bridgeRails, samples);
      continue;
    }
    const color = colorOf(segment);
    if (!edges.has(color)) edges.set(color, new Path2D());
    samplePath(edges.get(color), samples);
    samplePath(ballast, samples);
    addTies(ties, samples);
    addRails(rails, samples);
  }
  for (const [color, path] of edges) strokeWorld(g, path, rgba(color, 0.85), 4.2 * view.scale);
  strokeWorld(g, ballast, '#6E675D', 3.3 * view.scale);
  strokeWorld(g, ties, '#3B2F24', 0.26 * view.scale);
  strokeWorld(g, rails, '#D6D2CA', Math.max(1, 0.1 * view.scale));
  g.save();
  g.translate(2.6, -2.6);
  strokeWorld(g, shadows, 'rgba(0, 0, 0, 0.38)', 5.6 * view.scale);
  g.restore();
  strokeWorld(g, bents, '#3A2C1E', 0.5 * view.scale);
  strokeWorld(g, woodDecks, '#5C4632', 5 * view.scale);
  strokeWorld(g, steelDecks, '#474B50', 5 * view.scale);
  strokeWorld(g, woodSides, '#8A6C48', Math.max(1, 0.35 * view.scale));
  strokeWorld(g, steelSides, '#9AA2AA', Math.max(1, 0.45 * view.scale));
  strokeWorld(g, bridgeTies, '#2E241A', 0.26 * view.scale);
  strokeWorld(g, bridgeRails, '#D6D2CA', Math.max(1, 0.1 * view.scale));
  strokeWorld(g, tunnels, 'rgba(237, 231, 218, 0.35)', 3.3 * view.scale, [10 * view.scale, 6 * view.scale]);
  g.setLineDash([]);
}

function drawSwitches(g) {
  if (view.scale < SWITCH_SCALE || !switches.length || switchState.length !== switches.length) return;
  worldTransform(g);
  g.setLineDash([]);
  g.lineCap = 'butt';
  const box = visibleBox(40);
  const rails = view.scale >= RAILS_SCALE;
  const base = trackWidth();
  for (let k = 0; k < switches.length; k++) {
    const [x, z, normal, normalEnd, reverse, reverseEnd] = switches[k];
    if (x < box.minX || x > box.maxX || z < box.minZ || z > box.maxZ) continue;
    const thrown = switchState[k] === '1';
    const lined = segments[thrown ? reverse : normal];
    const other = segments[thrown ? normal : reverse];
    if (!lined || !other || lined.state !== 0 || other.state !== 0) continue;
    const reach = Math.min(22, Math.min(lined.length, other.length) * 0.7);
    const linedCurve = subCurve(lined.p, thrown ? reverseEnd : normalEnd, clamp(reach / lined.length, 0, 1));
    const otherCurve = subCurve(other.p, thrown ? normalEnd : reverseEnd, clamp(reach / other.length, 0, 1));
    const path = new Path2D();
    curveTo(path, linedCurve);
    if (rails) {
      strokeWorld(g, path, 'rgba(255, 250, 230, 0.55)', 0.8 * view.scale);
      continue;
    }
    const gap = new Path2D();
    curveTo(gap, otherCurve);
    strokeWorld(g, gap, CASING, base * ROLE_WIDTHS[other.role] + 0.8);
    strokeWorld(g, path, colorOf(lined), base * ROLE_WIDTHS[lined.role]);
  }
}

function townBoxes(g) {
  g.font = `600 15px ${FONT}`;
  return track.towns.map(town => {
    const w = g.measureText(town.n).width / 2 + 6;
    const x = sx(town.x);
    const y = sy(town.z);
    return { x0: x - w, y0: y - 12, x1: x + w, y1: y + 12 };
  });
}

function drawMapLabels(g, taken) {
  if ((!layers.names && !layers.limits) || !track.labels || view.scale < MAP_LABEL_SCALE) return;
  g.font = `italic 600 12px ${FONT}`;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.lineJoin = 'round';
  g.lineWidth = 3.5;
  const lineHeight = 13;
  for (const [lx, lz, text, angle, dim] of track.labels) {
    const limit = /^\d+$/.test(String(text).trim());
    if (limit ? !layers.limits : !layers.names) continue;
    const x = sx(lx);
    const y = sy(lz);
    if (x < -150 || y < -60 || x > width + 150 || y > height + 60) continue;
    const lines = String(text).split('\n');
    const half = Math.max(...lines.map(line => g.measureText(line).width)) / 2 + 3;
    const tall = lines.length * lineHeight / 2 + 2;
    const radians = -angle * Math.PI / 180;
    const cos = Math.abs(Math.cos(radians));
    const sin = Math.abs(Math.sin(radians));
    const hw = half * cos + tall * sin;
    const hh = half * sin + tall * cos;
    const box = { x0: x - hw, y0: y - hh, x1: x + hw, y1: y + hh };
    if (taken.some(other => box.x0 < other.x1 && box.x1 > other.x0 && box.y0 < other.y1 && box.y1 > other.y0)) continue;
    taken.push(box);
    g.save();
    g.translate(x, y);
    g.rotate(radians);
    g.globalAlpha = dim ? 0.5 : 1;
    g.strokeStyle = 'rgba(16, 14, 10, 0.85)';
    g.fillStyle = '#E8E1D0';
    lines.forEach((line, index) => {
      const ly = (index - (lines.length - 1) / 2) * lineHeight;
      g.strokeText(line, 0, ly);
      g.fillText(line, 0, ly);
    });
    g.restore();
  }
}

function drawTowns(g) {
  g.font = `600 15px ${FONT}`;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.lineJoin = 'round';
  g.lineWidth = 4;
  g.strokeStyle = 'rgba(16, 14, 10, 0.9)';
  g.fillStyle = '#F4EFE4';
  const pill = view.scale >= COUNT_SCALE ? 56 : 0;
  const icons = placeHits.map(hit => ({ x0: hit.x - hit.r - 2, y0: hit.y - hit.r - 2, x1: hit.x + hit.r + 2 + (hit.type === 'place' ? pill : 0), y1: hit.y + hit.r + 2 }));
  for (const town of track.towns) {
    const x = sx(town.x);
    const y = sy(town.z);
    if (x < -100 || y < -50 || x > width + 100 || y > height + 50) continue;
    const half = g.measureText(town.n).width / 2 + 2;
    const clear = dy => !icons.some(box => x - half < box.x1 && x + half > box.x0 && y + dy - 9 < box.y1 && y + dy + 9 > box.y0);
    const dy = [0, -24, 24, -36, 36, -48, 48].find(clear) ?? 0;
    g.strokeText(town.n, x, y + dy);
    g.fillText(town.n, x, y + dy);
  }
}

function drawRoute(g, time) {
  if (!layers.route) return;
  const points = routeFor(selected);
  if (!points || points.length < 4) return;
  g.beginPath();
  g.moveTo(sx(points[0]), sy(points[1]));
  for (let i = 2; i < points.length; i += 2) g.lineTo(sx(points[i]), sy(points[i + 1]));
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.strokeStyle = 'rgba(0, 0, 0, 0.85)';
  g.lineWidth = 10;
  g.setLineDash([]);
  g.stroke();
  let length = 0;
  for (let i = 2; i < points.length; i += 2) length += Math.hypot(sx(points[i]) - sx(points[i - 2]), sy(points[i + 1]) - sy(points[i - 1]));
  g.strokeStyle = ROUTE_COLOR;
  g.lineWidth = 6;
  g.setLineDash([16, 12]);
  g.lineDashOffset = -length - (time / 1000) * 40;
  g.stroke();
  g.setLineDash([]);
  const x = sx(points[points.length - 2]);
  const y = sy(points[points.length - 1]);
  g.strokeStyle = INK;
  g.lineWidth = 2;
  g.fillStyle = ROUTE_COLOR;
  g.beginPath();
  g.moveTo(x, y);
  g.lineTo(x, y - 30);
  g.lineTo(x + 20, y - 24);
  g.lineTo(x, y - 18);
  g.closePath();
  g.fill();
  g.stroke();
  g.fillRect(x - 1.5, y - 30, 3, 30);
  g.font = `bold 13px ${FONT}`;
  g.textAlign = 'left';
  g.textBaseline = 'middle';
  g.lineWidth = 4;
  g.strokeText(locoName(selected), x + 24, y - 24);
  g.fillText(locoName(selected), x + 24, y - 24);
}

function orderGroups() {
  if (!layers.orders || !selected || !panelInfo || panelInfo.target !== selected || !panelInfo.orders) return [];
  const orders = panelInfo.orders;
  const groups = [];
  orders.steps.forEach((step, index) => {
    if (!step.p || (step.s === 'done' && !orders.repeat)) return;
    const entry = { n: index + 1, t: step.t, current: orders.running && step.s === 'current' };
    const group = groups.find(item => Math.hypot(item.x - step.p[0], item.z - step.p[1]) < ORDER_MERGE);
    if (group) group.steps.push(entry);
    else groups.push({ x: step.p[0], z: step.p[1], steps: [entry] });
  });
  return groups;
}

function drawOrders(g) {
  orderHits = [];
  for (const group of orderGroups()) {
    const x = sx(group.x);
    const y = sy(group.z);
    if (x < -30 || y < -30 || x > width + 30 || y > height + 30) continue;
    const current = group.steps.some(step => step.current);
    const r = current ? 14 : 11;
    g.beginPath();
    g.arc(x, y, r + 2, 0, Math.PI * 2);
    g.fillStyle = 'rgba(0, 0, 0, 0.35)';
    g.fill();
    g.beginPath();
    g.arc(x, y, r, 0, Math.PI * 2);
    g.fillStyle = current ? '#FFE08C' : '#FFBF61';
    g.fill();
    g.font = `bold ${current ? 14 : 12}px ${FONT}`;
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.fillStyle = '#1A140D';
    g.fillText(group.steps.length > 1 ? `${group.steps[0].n}+` : `${group.steps[0].n}`, x, y + 0.5);
    orderHits.push({ x, y, r, group });
  }
}

function ordersHtml(group) {
  const lines = group.steps.map(step => `<div class="field${step.current ? ' current' : ''}">${step.n}. ${escapeHtml(step.t)}${step.current ? ' <b>(now)</b>' : ''}</div>`).join('');
  return `<div class="title">${escapeHtml(locoName(selected) || 'Train')}'s orders</div>${lines}`;
}

function pathOf(indices) {
  const path = new Path2D();
  for (const index of indices) {
    const segment = segments[index];
    if (segment) curveTo(path, segment.p);
  }
  return path;
}

function drawCondition(g) {
  if (!layers.condition || !paths || !track || !segmentInfo || segmentInfo.sv !== track.version) return;
  if (conditionPaths.key !== segmentInfo) {
    const degraded = [];
    const severe = [];
    segmentInfo.s.forEach((info, index) => {
      if (!info || !info[5] || !segments[index] || segments[index].state !== 0) return;
      (info[5] === 2 ? severe : degraded).push(index);
    });
    conditionPaths = { key: segmentInfo, degraded: pathOf(degraded), severe: pathOf(severe) };
  }
  worldTransform(g);
  g.lineCap = 'butt';
  const width = trackWidth() + 3;
  strokeWorld(g, conditionPaths.degraded, '#F2C230', width);
  strokeWorld(g, conditionPaths.severe, '#C2562B', width);
  g.setLineDash([]);
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function drawDestinations(g) {
  if (!layers.destinations || !paths || !track || !selected || !panelInfo || panelInfo.target !== selected || !panelInfo.dsg) return;
  const dsg = panelInfo.dsg;
  if (!dsg.c.length && !dsg.s.length) return;
  const key = `${selected}|${track.version}|${dsg.c.join(',')}|${dsg.s.join(',')}`;
  if (destinationPaths.key !== key) destinationPaths = { key, coming: pathOf(dsg.c), spotted: pathOf(dsg.s) };
  worldTransform(g);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  const width = Math.max(trackWidth() + 7, 3.4 * view.scale + 5);
  strokeWorld(g, destinationPaths.coming, rgba(COMING_COLOR, 0.55), width);
  strokeWorld(g, destinationPaths.spotted, rgba(SPOTTED_COLOR, 0.55), width);
  g.setLineDash([]);
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function highlightName() {
  if (!highlight) return 'Destination';
  const info = segmentInfo && track && segmentInfo.sv === track.version ? segmentInfo.s[highlight.s[0]] : null;
  const place = info && info[4] >= 0 ? segmentInfo.pl[info[4]] : null;
  return place ? place[0] : 'Destination';
}

function drawHighlight(g, time) {
  if (!highlight || !paths || !track || !highlight.s.length) return;
  const key = `${highlight.v}|${track.version}`;
  if (highlightPath.key !== key) highlightPath = { key, path: pathOf(highlight.s) };
  const beat = 0.5 + 0.5 * Math.sin(time / 1200 * Math.PI * 2);
  worldTransform(g);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  const glow = Math.max(trackWidth() + 6, 2.4 * view.scale + 6) * (0.9 + 0.35 * beat);
  for (const [spread, alpha] of [[4.2, 0.07], [3, 0.12], [2, 0.22], [1.3, 0.4]]) {
    strokeWorld(g, highlightPath.path, rgba(highlight.c, alpha * (0.75 + 0.5 * beat)), glow * spread);
  }
  strokeWorld(g, highlightPath.path, highlight.c, trackWidth() + 3);
  strokeWorld(g, highlightPath.path, '#FFFFFF', Math.max(1.2, trackWidth() * 0.45));
  g.setLineDash([]);
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  const x = sx(highlight.x);
  const y = sy(highlight.z);
  const phase = (time / 1400) % 1;
  for (const shift of [0, 0.5]) {
    const step = (phase + shift) % 1;
    g.beginPath();
    g.arc(x, y, 12 + 46 * step, 0, Math.PI * 2);
    g.strokeStyle = rgba(highlight.c, 0.85 * (1 - step));
    g.lineWidth = 3;
    g.stroke();
  }
}

function drawHighlightArrow(g) {
  highlightArrow = null;
  if (!highlight || !highlight.s.length) return;
  const x = sx(highlight.x);
  const y = sy(highlight.z);
  const margin = 46;
  if (x >= margin && y >= margin && x <= width - margin && y <= height - margin) return;
  const cx = width / 2;
  const cy = height / 2;
  const dx = x - cx;
  const dy = y - cy;
  const reach = Math.min(Math.abs(dx) > 0.01 ? (width / 2 - margin) / Math.abs(dx) : Infinity, Math.abs(dy) > 0.01 ? (height / 2 - margin) / Math.abs(dy) : Infinity);
  const ax = cx + dx * reach;
  const ay = cy + dy * reach;
  const angle = Math.atan2(dy, dx);
  g.save();
  g.translate(ax, ay);
  g.rotate(angle);
  g.beginPath();
  g.moveTo(20, 0);
  g.lineTo(-10, -14);
  g.lineTo(-4, 0);
  g.lineTo(-10, 14);
  g.closePath();
  g.fillStyle = highlight.c;
  g.strokeStyle = INK;
  g.lineWidth = 2;
  g.shadowColor = highlight.c;
  g.shadowBlur = 14;
  g.fill();
  g.shadowBlur = 0;
  g.stroke();
  g.restore();
  const feet = Math.hypot(highlight.x - view.x, highlight.z - view.z) * 3.28084;
  const distance = feet >= 1000 ? `${(feet / 5280).toFixed(1)} mi` : `${Math.round(feet)} ft`;
  const label = `${highlightName()} · ${distance}`;
  g.font = `bold 12px ${FONT}`;
  const w = g.measureText(label).width + 16;
  const lx = clamp(ax - w / 2 - Math.cos(angle) * 40, 6, width - w - 6);
  const ly = clamp(ay - 11 - Math.sin(angle) * 34, 6, height - 28);
  g.beginPath();
  roundRect(g, lx, ly, w, 22, 11);
  g.fillStyle = 'rgba(22, 19, 14, 0.92)';
  g.fill();
  g.strokeStyle = highlight.c;
  g.lineWidth = 1.5;
  g.stroke();
  g.fillStyle = '#F4EFE4';
  g.textAlign = 'left';
  g.textBaseline = 'middle';
  g.fillText(label, lx + 8, ly + 11.5);
  highlightArrow = { x: ax, y: ay, r: 24 };
}

function drawFusees(g, time) {
  const phase = (time / 1200) % 1;
  const beat = 0.5 + 0.5 * Math.sin(time / 1200 * Math.PI * 2);
  for (const fusee of fusees) {
    const x = sx(fusee[0]);
    const y = sy(fusee[1]);
    if (x < -45 || y < -45 || x > width + 45 || y > height + 45) continue;
    g.beginPath();
    g.arc(x, y, 14 + 24 * phase, 0, Math.PI * 2);
    g.strokeStyle = `rgba(255, 110, 40, ${0.75 * (1 - phase)})`;
    g.lineWidth = 4;
    g.stroke();
    const r = 14 * (1 + 0.12 * beat);
    g.beginPath();
    g.arc(x, y, r, 0, Math.PI * 2);
    g.fillStyle = 'rgba(28, 24, 22, 0.94)';
    g.fill();
    g.lineWidth = 2;
    g.strokeStyle = '#F0ECE4';
    g.stroke();
    flame(g, x, y + r * 0.08, r * 0.62, '#EC4A22');
    flame(g, x, y + r * 0.22, r * 0.34, '#FFD65C');
  }
}

function flame(g, x, y, size, color) {
  g.beginPath();
  g.moveTo(x, y - size * 1.25);
  g.bezierCurveTo(x + size * 0.35, y - size * 0.55, x + size * 0.95, y - size * 0.1, x + size * 0.82, y + size * 0.4);
  g.arc(x, y + size * 0.35, size * 0.82, 0, Math.PI, false);
  g.bezierCurveTo(x - size * 0.95, y - size * 0.1, x - size * 0.2, y - size * 0.6, x, y - size * 1.25);
  g.closePath();
  g.fillStyle = color;
  g.fill();
}

function nearestFusee(x, z, reach) {
  let best = null;
  let bestDistance = reach;
  for (const fusee of fusees) {
    const distance = Math.hypot(fusee[0] - x, fusee[1] - z);
    if (distance < bestDistance) {
      bestDistance = distance;
      best = fusee;
    }
  }
  return best;
}

function showMenu(px, py, title, items) {
  menu.innerHTML = (title ? `<div class="title">${escapeHtml(title)}</div>` : '')
    + items.map((item, index) => `<button data-item="${index}"${item.disabled ? ' disabled' : ''}>${escapeHtml(item.label)}${item.sub ? `<span class="sub">${escapeHtml(item.sub)}</span>` : ''}</button>`).join('');
  menu.onclick = event => {
    const button = event.target.closest('[data-item]');
    if (!button || button.disabled) return;
    hideMenu();
    items[Number(button.dataset.item)].run();
  };
  menu.classList.remove('hidden');
  menu.style.left = `${Math.max(8, Math.min(px, width - menu.offsetWidth - 8))}px`;
  menu.style.top = `${Math.max(8, Math.min(py, height - menu.offsetHeight - 8))}px`;
  tooltip.classList.add('hidden');
}

function hideMenu() {
  menu.classList.add('hidden');
}

function showToast(text, error) {
  toast.textContent = text;
  toast.classList.toggle('error', !!error);
  toast.classList.remove('hidden');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.add('hidden'), 3500);
}

async function fuseeAction(action, x, z, key) {
  try {
    const headers = { 'X-Map-Plus-Action': action, 'X-Map-Plus-Point': `${x.toFixed(2)},${z.toFixed(2)}` };
    if (key) headers['X-Map-Plus-Fusee'] = key;
    const response = await fetch(API + 'fusee', { cache: 'no-store', headers });
    const result = await response.json();
    showToast(result.text || (result.ok ? 'Done' : 'That didn\'t work'), !result.ok);
  } catch (error) {
    showToast("Couldn't reach the game", true);
  }
}

async function sendHere(train, x, z) {
  try {
    const headers = { 'X-Map-Plus-Train': train, 'X-Map-Plus-Point': `${x.toFixed(2)},${z.toFixed(2)}` };
    const response = await fetch(API + 'send', { cache: 'no-store', headers });
    const result = await response.json();
    showToast(result.text || (result.ok ? 'Sent' : 'That didn\'t work'), !result.ok);
  } catch (error) {
    showToast("Couldn't reach the game", true);
  }
}

const ORDER_ICONS = {
  goto: '<path d="M12 21s-6-5.6-6-10.5a6 6 0 0 1 12 0C18 15.4 12 21 12 21z"/><circle cx="12" cy="10.5" r="2.2"/>',
  pickup: '<rect x="3" y="11" width="11" height="6" rx="1"/><path d="M5.5 19.5h.01M11.5 19.5h.01M15 9h6M18 6l3 3-3 3"/>',
  cut: '<rect x="2" y="9" width="8" height="6" rx="1"/><rect x="14" y="9" width="8" height="6" rx="1"/><path d="M11 12h2"/>',
  wait: '<circle cx="12" cy="12" r="8"/><path d="M12 7v5l3 2"/>',
  speed: '<path d="M4.6 16.5a8.5 8.5 0 1 1 14.8 0"/><path d="M12 14l4-4.5"/><circle cx="12" cy="14" r="1.4"/>',
  goback: '<path d="M8 6.5A7 7 0 1 1 5.6 15"/><path d="M8 6.5 4.5 5.5"/><path d="M8 6.5 7.6 10.2"/>',
  stations: '<circle cx="12" cy="5.5" r="2.5"/><path d="M8 21v-6l-1-4h10l-1 4v6"/>',
  refuel: '<path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z"/>',
  turntable: '<circle cx="12" cy="12" r="8"/><path d="M5 12h14"/><circle cx="12" cy="12" r="1.5"/>',
  up: '<path d="M6 15l6-6 6 6"/>',
  down: '<path d="M6 9l6 6 6-6"/>',
  trash: '<path d="M4 7h16M10 7V4h4v3M6 7l1 13h10l1-13"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3"/>',
  paste: '<rect x="6" y="4" width="12" height="17" rx="2"/><path d="M9 4h6v3H9z"/>'
};

const ordersPanel = document.getElementById('orders');
let ordersApi = false;
let ordersSupport = 0;
const ORDERS_API = 1;
const UPDATE_ORDERS = 'Update Train Orders to edit its orders from the web map';
let ordersOpen = false;
let ordersTrain = null;
let ordersView = null;
let ordersTab = 'orders';
let ordersMenu = null;
let ordersMenus = [];
let menuHover = null;
let jobDraft = null;
let ghostIndex = null;
let ghost = null;
const GHOST_COLOR = '#40D9FF';
const STRIP_CAR = 16;
const STRIP_ENGINE = 26;
const STRIP_GAP = 2;
const STRIP_ARROW = 14;
const STRIP_MIN_SCALE = 0.55;
const SILHOUETTE_KINDS = ['steam', 'diesel', 'tender', 'box', 'hopper', 'tank', 'flat', 'gondola', 'coach', 'caboose', 'other', 'unknown'];

function silhouetteShapes(kind) {
  const w = kind === 'steam' || kind === 'diesel' ? 88 : 52;
  const wheels = [['c', 9, 5, 4], ['c', w - 9, 5, 4]];
  const coach = [];
  for (let x = 8; x + 5 < w - 6; x += 9) coach.push(['r', x, 24, x + 5, 31]);
  switch (kind) {
    case 'steam':
      return { w, fill: [['rr', 12, 14, 64, 30, 7], ['r', 4, 12, 15, 31], ['r', 11, 29, 18, 38], ['r', 34, 29, 42, 35], ['r', 62, 9, 84, 36], ['r', 60, 35, 86, 39], ['r', 2, 7, 86, 11], ['c', 22, 7, 6], ['c', 37, 7, 6], ['c', 52, 7, 6], ['c', 75, 5, 4]], cut: [] };
    case 'diesel':
      return { w, fill: [['r', 2, 8, 86, 12], ['r', 4, 12, 15, 27], ['r', 14, 12, 33, 37], ['r', 32, 12, 84, 30], ['c', 14, 5, 4], ['c', 25, 5, 4], ['c', 63, 5, 4], ['c', 74, 5, 4]], cut: [['r', 18, 27, 29, 33]] };
    case 'tender':
      return { w, fill: [['r', 3, 9, w - 3, 28], ['rr', 8, 26, w - 8, 33, 3], ...wheels], cut: [] };
    case 'box':
      return { w, fill: [['r', 2, 9, w - 2, 37], ...wheels], cut: [['r', w / 2 - 1, 13, w / 2 + 1, 33]] };
    case 'hopper':
      return { w, fill: [['p', 2, 36, w - 2, 36, w - 2, 22, w / 2 + 7, 10, w / 2 - 7, 10, 2, 22], ...wheels], cut: [] };
    case 'tank':
      return { w, fill: [['r', 3, 9, w - 3, 13], ['rr', 2, 12, w - 2, 30, 9], ['r', w / 2 - 5, 28, w / 2 + 5, 35], ...wheels], cut: [] };
    case 'flat':
      return { w, fill: [['r', 1, 10, w - 1, 16], ...wheels], cut: [] };
    case 'gondola':
      return { w, fill: [['r', 2, 9, w - 2, 24], ...wheels], cut: [] };
    case 'coach':
      return { w, fill: [['rr', 2, 9, w - 2, 37, 6], ...wheels], cut: coach };
    case 'caboose':
      return { w, fill: [['r', 3, 9, w - 3, 30], ['r', w / 2 - 9, 29, w / 2 + 9, 39], ...wheels], cut: [] };
    case 'unknown':
      return { w, fill: [['rr', 2, 9, w - 2, 37, 4], ...wheels], cut: [['rr', 6, 13, w - 6, 33, 2]] };
    default:
      return { w, fill: [['rr', 2, 9, w - 2, 34, 4], ...wheels], cut: [] };
  }
}

function silhouetteSvg(kind) {
  const h = 40;
  const { w, fill, cut } = silhouetteShapes(kind);
  const shape = ([type, ...v]) => {
    if (type === 'r') return `<rect x="${v[0]}" y="${h - v[3]}" width="${v[2] - v[0]}" height="${v[3] - v[1]}"/>`;
    if (type === 'rr') return `<rect x="${v[0]}" y="${h - v[3]}" width="${v[2] - v[0]}" height="${v[3] - v[1]}" rx="${v[4]}"/>`;
    if (type === 'c') return `<circle cx="${v[0]}" cy="${h - v[1]}" r="${v[2]}"/>`;
    const points = [];
    for (let i = 0; i < v.length; i += 2) points.push(`${v[i]},${h - v[i + 1]}`);
    return `<polygon points="${points.join(' ')}"/>`;
  };
  const mask = cut.length ? `<mask id="m"><rect width="${w}" height="${h}" fill="#fff"/><g fill="#000">${cut.map(shape).join('')}</g></mask>` : '';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none">${mask}<g fill="#fff"${cut.length ? ' mask="url(#m)"' : ''}>${fill.map(shape).join('')}</g></svg>`;
}

function installSilhouettes() {
  const style = document.createElement('style');
  style.textContent = SILHOUETTE_KINDS.map(kind => {
    const url = `url("data:image/svg+xml,${encodeURIComponent(silhouetteSvg(kind))}")`;
    return `.orders-panel .strip .strip-car i.${kind}{-webkit-mask-image:${url};mask-image:${url}}`;
  }).join('\n');
  document.head.appendChild(style);
}

installSilhouettes();

async function loadGhost(index) {
  const train = ordersTrain;
  const result = await ordersCall('ghost', { index });
  if (ghostIndex !== index || ordersTrain !== train) return;
  ghost = result && result.ok && Array.isArray(result.cars) && result.cars.length ? { index, train, cars: result.cars } : null;
}

function setGhost(index) {
  if (index === ghostIndex) return;
  ghostIndex = index;
  ghost = null;
  if (index !== null && ordersOpen && ordersTrain) loadGhost(index);
}

function drawGamePaths(g) {
  if (!gamePaths || !gamePaths.length) return;
  const wide = Math.max(MIN_CAR_PIXELS, CAR_WIDTH * view.scale);
  g.lineJoin = 'round';
  g.lineCap = 'butt';
  for (const path of gamePaths) {
    const p = path.p;
    if (p.length < 4) continue;
    g.beginPath();
    for (let i = 0; i + 1 < p.length; i += 2) {
      const x = sx(p[i]);
      const y = sy(p[i + 1]);
      if (i) g.lineTo(x, y);
      else g.moveTo(x, y);
    }
    g.strokeStyle = 'rgba(6, 28, 36, 0.8)';
    g.lineWidth = wide + 3;
    g.stroke();
    g.strokeStyle = rgba(path.c, 0.72);
    g.lineWidth = wide;
    g.stroke();
  }
}

function drawGhost(g) {
  if (!ghost || !ordersOpen || ghost.train !== ordersTrain || ghost.index !== ghostIndex) return;
  const wide = Math.max(MIN_CAR_PIXELS, CAR_WIDTH * view.scale);
  g.lineJoin = 'round';
  g.lineCap = 'butt';
  for (const car of ghost.cars) {
    const p = car.p;
    const points = [];
    for (let i = 0; i + 1 < p.length; i += 2) points.push([sx(p[i]), sy(p[i + 1])]);
    if (points.length < 2) continue;
    g.beginPath();
    points.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y)));
    g.shadowColor = GHOST_COLOR;
    g.shadowBlur = 10;
    g.strokeStyle = 'rgba(6, 28, 36, 0.8)';
    g.lineWidth = wide + 3;
    g.stroke();
    g.shadowBlur = 0;
    g.strokeStyle = rgba(GHOST_COLOR, 0.72);
    g.lineWidth = wide;
    g.stroke();
    if (!car.e) continue;
    const [ax, ay] = points[0];
    const [bx, by] = points[points.length - 1];
    const length = Math.hypot(bx - ax, by - ay);
    if (length < wide) continue;
    const ux = (bx - ax) / length;
    const uy = (by - ay) / length;
    const [cx, cy] = points[Math.floor(points.length / 2)];
    const size = wide * 0.32;
    g.beginPath();
    g.moveTo(cx - ux * size - uy * size, cy - uy * size + ux * size);
    g.lineTo(cx + ux * size, cy + uy * size);
    g.lineTo(cx - ux * size + uy * size, cy - uy * size - ux * size);
    g.strokeStyle = 'rgba(6, 28, 36, 0.9)';
    g.lineWidth = Math.max(1.5, wide * 0.16);
    g.lineCap = 'round';
    g.stroke();
    g.lineCap = 'butt';
  }
}

function icon(name) {
  return `<svg viewBox="0 0 24 24">${ORDER_ICONS[name] || ''}</svg>`;
}

function plainText(text) {
  return String(text || '').replace(/<[^>]*>/g, '');
}

async function ordersCall(action, args) {
  const train = ordersTrain;
  if (!train) return null;
  const headers = { 'X-Map-Plus-Train': train, 'X-Map-Plus-Action': action };
  if (args) headers['X-Map-Plus-Args'] = encodeURIComponent(JSON.stringify(args));
  try {
    const response = await fetch(API + 'orders', { cache: 'no-store', headers });
    const result = await response.json();
    return ordersTrain === train ? result : null;
  } catch (error) {
    return { ok: false, text: "Couldn't reach the game" };
  }
}

async function ordersAction(action, args) {
  const result = await ordersCall(action, args);
  if (!result) return;
  if (result.text) showToast(result.text, !result.ok);
  if (result.rows) {
    ordersView = checkedView(result);
    renderOrders();
  }
}

async function refreshOrders() {
  const result = await ordersCall('view');
  if (!result || !ordersOpen) return;
  if (result.rows) ordersView = checkedView(result);
  else ordersView = { error: result.text || "Couldn't read the orders" };
  if (!ordersBusy()) renderOrders();
  if (ghostIndex !== null) loadGhost(ghostIndex);
}

function ordersBusy() {
  const active = document.activeElement;
  return !!ordersMenu || (active && ordersPanel.contains(active) && (active.tagName === 'SELECT' || active.tagName === 'INPUT'));
}

async function ordersLoop() {
  for (;;) {
    if (ordersOpen) {
      if (selected !== ordersTrain) {
        ordersTrain = selected;
        ordersView = null;
        jobDraft = null;
        setGhost(null);
        hideOrdersMenu();
        renderOrders();
      }
      if (ordersTrain && !ordersBusy()) await refreshOrders();
    }
    await sleep(1000);
  }
}

function setOrdersOpen(open) {
  ordersOpen = open && ordersApi;
  ordersPanel.classList.toggle('hidden', !ordersOpen);
  document.body.classList.toggle('orders-open', ordersOpen);
  document.querySelector('[data-action="orders"]').classList.toggle('on', ordersOpen);
  hideOrdersMenu();
  setGhost(null);
  if (!ordersOpen) return;
  ordersTrain = selected;
  ordersView = null;
  jobDraft = null;
  renderOrders();
  if (ordersTrain) refreshOrders();
}

function setOrdersSupport(level) {
  if (ordersSupport === level) return;
  ordersSupport = level;
  ordersApi = level === 2;
  document.querySelector('[data-action="orders"]').classList.toggle('hidden', level === 0);
  if (!ordersApi && ordersOpen) setOrdersOpen(false);
  if (selected) renderPanel();
}

function toggleOrders() {
  if (ordersSupport === 1) {
    showToast(UPDATE_ORDERS, true);
    return;
  }
  if (!selected && !ordersOpen) showToast('Select a train first');
  setOrdersOpen(!ordersOpen);
}

function checkedView(result) {
  return (result.api || 0) >= ORDERS_API ? result : { error: UPDATE_ORDERS };
}

function stripIsEngine(car) {
  return car.k === 'steam' || car.k === 'diesel' || car.k === 'tender';
}

function stripCarWidth(car) {
  return car.k === 'steam' || car.k === 'diesel' ? STRIP_ENGINE : STRIP_CAR;
}

function stripCountWidth(count) {
  return `+${count}`.length * 6.5 + 6;
}

function stripMeasure(cars, tokens) {
  let total = 0;
  for (const token of tokens) total += (token.hidden ? stripCountWidth(token.hidden) : stripCarWidth(cars[token.index])) + STRIP_GAP;
  return total - STRIP_GAP;
}

function stripCollapse(cars, context, tight) {
  const same = (i, index) => i >= 0 && i < cars.length && !!cars[i].n === !!cars[index].n && !!cars[i].l === !!cars[index].l && !stripIsEngine(cars[i]);
  const edge = index => {
    let before = 0;
    while (same(index - before - 1, index)) before++;
    let after = 0;
    while (same(index + after + 1, index)) after++;
    return Math.min(before, after);
  };
  const keep = new Array(cars.length).fill(false);
  cars.forEach((car, i) => {
    if (!stripIsEngine(car) && !car.n && !car.l) return;
    if (tight && !stripIsEngine(car) && edge(i) >= 2) return;
    for (let j = Math.max(0, i - context); j <= Math.min(cars.length - 1, i + context); j++) keep[j] = true;
  });
  const tokens = [];
  let hidden = 0;
  cars.forEach((car, i) => {
    if (!keep[i]) {
      hidden++;
      return;
    }
    if (hidden) tokens.push({ hidden });
    hidden = 0;
    tokens.push({ index: i });
  });
  if (hidden) tokens.push({ hidden });
  return tokens;
}

function stripFit(cars, width) {
  const all = cars.map((car, index) => ({ index }));
  if (stripMeasure(cars, all) <= width) return { tokens: all, scale: 1 };
  for (let context = 2; context >= 0; context--) {
    const tokens = stripCollapse(cars, context, false);
    if (stripMeasure(cars, tokens) <= width) return { tokens, scale: 1 };
  }
  const tokens = stripCollapse(cars, 0, true);
  const measured = stripMeasure(cars, tokens);
  return { tokens, scale: measured > width ? Math.max(STRIP_MIN_SCALE, width / measured) : 1 };
}

function stripSide() {
  const scroller = ordersPanel.querySelector('[data-orders-scroll]');
  const width = scroller && scroller.clientWidth > 100 ? scroller.clientWidth - 8 : 380;
  return Math.max(60, (width - 16 - STRIP_ARROW) / 2);
}

function stripHtml(strip, side) {
  const cars = list => {
    const { tokens, scale } = stripFit(list, side);
    return tokens.map(token => {
      if (token.hidden) return `<span class="more" style="width:${stripCountWidth(token.hidden)}px">+${token.hidden}</span>`;
      const car = list[token.index];
      return `<span class="strip-car${car.n ? ' new' : ''}${car.l ? ' leaving' : ''}" style="width:${(stripCarWidth(car) * scale).toFixed(1)}px;margin-right:${(STRIP_GAP * scale).toFixed(1)}px"><i class="${SILHOUETTE_KINDS.includes(car.k) ? car.k : 'other'}" style="background:${car.c}"></i></span>`;
    }).join('');
  };
  const arrow = '<svg viewBox="0 0 28 48"><polygon points="3,44 11,44 25,24 11,4 3,4 17,24"/></svg>';
  return `<div class="strip${strip.p ? ' problem' : ''}" title="${escapeHtml(plainText(strip.tip))}"><span class="side" style="width:${side.toFixed(1)}px">${cars(strip.b)}</span><span class="arrow">${arrow}</span><span class="side" style="width:${side.toFixed(1)}px">${cars(strip.a)}</span></div>`;
}

function controlHtml(control, index) {
  if (control.k === 'smart') {
    return `<label class="order-control"><input type="checkbox" data-orders-smart="${index}"${control.v ? ' checked' : ''}>${escapeHtml(control.l)}</label>`;
  }
  if (control.k === 'time') {
    const hour = Math.floor(control.v / 60);
    const minute = control.v % 60;
    const hours = control.hours.map((label, value) => `<option value="${value}"${value === hour ? ' selected' : ''}>${escapeHtml(label)}</option>`).join('');
    const minutes = control.minutes.map(value => `<option value="${value}"${value === minute ? ' selected' : ''}>:${String(value).padStart(2, '0')}</option>`).join('');
    return `<label class="order-control">${escapeHtml(control.l)}<select data-orders-time="hour" data-index="${index}">${hours}</select><select data-orders-time="minute" data-index="${index}">${minutes}</select></label>`;
  }
  const options = control.o.map(option => `<option value="${escapeHtml(option.v)}"${option.v === control.v ? ' selected' : ''}>${escapeHtml(option.l)}</option>`).join('');
  return `<label class="order-control">${control.l ? escapeHtml(control.l) : ''}<select data-orders-set="${control.k}" data-index="${index}">${options}</select></label>`;
}

function ordersBodyHtml(view) {
  const parts = [];
  parts.push(kv([['Status', richText(view.status)], ['Engineer', richText(view.engineer)], view.train ? ['Train', richText(view.train)] : null]));
  const toggle = (key, label, on, hint) => `<label class="switch-row" title="${escapeHtml(hint)}"><span>${label}</span><input type="checkbox" data-orders-toggle="${key}"${on ? ' checked' : ''}><i class="switch"></i></label>`;
  parts.push(`<div class="toggles">${toggle('repeat', 'Repeat', view.repeat, 'Each order goes back to the end of the list when it\'s done, so the orders run over and over.')}${toggle('pass', 'Smart signals', view.pass, 'In ABS mode, when the train has waited 10 seconds at a red signal at a switch, the engineer passes it at restricted speed, but only when no other train is in the blocks ahead.')}</div>`);
  parts.push(`<div class="note">While this panel is open, the orders you give from the map (right-click a track or a car) are added to the list, as with the game's Train Orders window open.</div>`);
  parts.push('<h3>Orders</h3>');
  const side = stripSide();
  const rows = view.rows.map((row, index) => {
    const text = richText(row.t) + (row.d ? ` (${richText(row.d)})` : '');
    const controls = row.c.map(control => controlHtml(control, index)).join('');
    return `<div class="order ${row.s}" data-row="${index}"><div class="order-line"><span class="order-text">${text}</span><span class="order-buttons">`
      + `<button class="icon" data-orders="move" data-index="${index}" data-offset="-1" title="Run this order earlier"${index === 0 ? ' disabled' : ''}>${icon('up')}</button>`
      + `<button class="icon" data-orders="move" data-index="${index}" data-offset="1" title="Run this order later"${index === view.rows.length - 1 ? ' disabled' : ''}>${icon('down')}</button>`
      + `<button class="icon" data-orders="remove" data-index="${index}" title="Remove this order">${icon('trash')}</button>`
      + `</span></div>${controls}${row.f ? stripHtml(row.f, side) : ''}</div>`;
  }).join('');
  parts.push(`<div class="scroll" data-orders-scroll>${rows || '<div class="empty">No orders yet</div>'}</div>`);
  parts.push('<h3>Add</h3>');
  parts.push(`<div class="add">${view.menus.map(menu => `<button class="icon" data-orders-menu="${menu.id}" title="${escapeHtml(menu.title)}: ${escapeHtml(menu.tip)}">${icon(menu.id)}</button>`).join('')}</div>`);
  parts.push('<div class="strip-buttons">'
    + `<button data-orders="run"${view.count ? '' : ' disabled'}>${view.running ? 'Pause' : 'Start'}</button>`
    + `<button data-orders="skip"${view.finished ? ' disabled' : ''}>Skip</button>`
    + `<button data-orders="clear"${view.count ? '' : ' disabled'}>Clear</button>`
    + '<span class="spacer"></span>'
    + `<button class="icon" data-orders="copy" title="Copy these orders to paste them into another locomotive's orders">${icon('copy')}</button>`
    + `<button class="icon" data-orders="paste" title="${escapeHtml(view.paste ? `Add the ${view.paste} to the end of these orders` : "Copy another locomotive's orders first")}"${view.paste ? '' : ' disabled'}>${icon('paste')}</button>`
    + '</div>');
  return parts.join('');
}

function jobsBodyHtml(view) {
  const parts = [];
  parts.push('<h3>Save these orders</h3>');
  const name = jobDraft ?? view.jobName ?? '';
  parts.push(`<div class="job-save"><input type="text" data-job-name maxlength="40" value="${escapeHtml(name)}" placeholder="Job name"><button data-orders="save"${view.count ? '' : ' disabled'} title="${view.count ? 'Save these orders under this name. A saved job with the same name is replaced.' : 'Add some orders first'}">Save</button></div>`);
  parts.push('<div class="note">Saved jobs are kept outside the save, so any engine in any game can run them. Orders that couple to a car only work where that car is: for jobs you run again, use Pick up, Leave cars and Couple back.</div>');
  parts.push('<h3>Saved jobs</h3>');
  const jobs = view.jobs.map(job => `<div class="job" title="${escapeHtml(plainText(job.tip))}"><span>${richText(job.l)}</span>`
    + `<button data-orders="load" data-name="${escapeHtml(job.n)}"${view.running ? ' disabled title="Pause the orders first"' : ` title="Replace ${escapeHtml(view.name)}'s orders with this job"`}>Load</button>`
    + `<button data-orders="add" data-name="${escapeHtml(job.n)}" title="Add this job to the end of ${escapeHtml(view.name)}'s orders">Add</button>`
    + `<button class="icon" data-orders="delete" data-name="${escapeHtml(job.n)}" title="Delete this saved job">${icon('trash')}</button></div>`).join('');
  parts.push(`<div class="scroll">${jobs || '<div class="empty">No saved jobs yet</div>'}</div>`);
  return parts.join('');
}

function renderOrders() {
  if (!ordersOpen) return;
  const scroller = ordersPanel.querySelector('[data-orders-scroll]');
  const scroll = scroller ? scroller.scrollTop : 0;
  const view = ordersView;
  const name = view && view.name ? view.name : ordersTrain ? locoName(ordersTrain) || 'Train' : 'No train selected';
  const parts = [`<header><div class="heading"><h2 class="list">Train Orders</h2><div class="sub">${escapeHtml(name)}</div></div><button class="close" data-orders-close title="Close (O)">×</button></header>`];
  if (!ordersTrain) parts.push('<div class="empty">Select a train to see and edit its orders.</div>');
  else if (!view) parts.push('<div class="empty">Loading the orders…</div>');
  else if (view.error) parts.push(`<div class="empty">${escapeHtml(view.error)}</div>`);
  else {
    parts.push(`<div class="tabs"><button data-orders-tab="orders"${ordersTab === 'orders' ? ' class="on"' : ''}>Orders</button><button data-orders-tab="jobs"${ordersTab === 'jobs' ? ' class="on"' : ''}>Saved jobs</button></div>`);
    parts.push(`<div class="body">${ordersTab === 'orders' ? ordersBodyHtml(view) : jobsBodyHtml(view)}</div>`);
  }
  ordersPanel.innerHTML = parts.join('');
  const next = ordersPanel.querySelector('[data-orders-scroll]');
  if (next) next.scrollTop = scroll;
}

async function openOrdersMenu(id, anchor) {
  const result = await ordersCall('menu', { menu: id, path: [] });
  if (!result) return;
  if (!result.ok) {
    showToast(result.text || "Couldn't open the menu", true);
    return;
  }
  hideOrdersMenu();
  const menu = ordersView && ordersView.menus ? ordersView.menus.find(item => item.id === id) : null;
  ordersMenu = { id, anchor, title: menu ? menu.title : '' };
  addMenuLevel([], result.items, null);
}

function addMenuLevel(path, items, parentButton) {
  const level = ordersMenus.length;
  const el = document.createElement('div');
  el.className = 'menu orders-menu';
  const rows = items.map((item, index) => `<button data-menu-item="${index}"${item.d ? ' disabled' : ''}${item.s ? ` title="${escapeHtml(plainText(item.s))}"` : ''}><span class="label">${item.c ? '✓ ' : ''}${richText(item.l)}</span>${item.s ? `<span class="sub">${richText(item.s)}</span>` : ''}${item.sub ? '<span class="more">›</span>' : ''}</button>`).join('');
  el.innerHTML = (level === 0 ? `<div class="title">${escapeHtml(ordersMenu.title)}</div>` : '') + (rows || '<div class="title">Nothing here</div>');
  document.body.appendChild(el);
  const entry = { el, path, items, open: null };
  ordersMenus.push(entry);
  const w = el.offsetWidth;
  const h = el.offsetHeight;
  let left;
  let top;
  if (!parentButton) {
    const box = ordersMenu.anchor.getBoundingClientRect();
    left = Math.max(8, Math.min(box.left, window.innerWidth - w - 8));
    top = box.bottom + 6 + h > window.innerHeight - 8 ? Math.max(8, box.top - h - 6) : box.bottom + 6;
  } else {
    const row = parentButton.getBoundingClientRect();
    const parent = parentButton.closest('.orders-menu').getBoundingClientRect();
    left = parent.left - w - 2 >= 8 ? parent.left - w - 2 : Math.min(parent.right + 2, window.innerWidth - w - 8);
    top = Math.max(8, Math.min(row.top - 4, window.innerHeight - h - 8));
  }
  el.style.left = `${left}px`;
  el.style.top = `${top}px`;
  el.addEventListener('click', event => {
    const button = event.target.closest('[data-menu-item]');
    if (!button || button.disabled) return;
    const index = Number(button.dataset.menuItem);
    const item = entry.items[index];
    if (!item) return;
    if (item.sub) {
      openSubmenu(entry, index, button);
      return;
    }
    const id = ordersMenu.id;
    hideOrdersMenu();
    ordersAction('pick', { menu: id, path: [...entry.path, index] });
  });
  el.addEventListener('mouseover', event => {
    const button = event.target.closest('[data-menu-item]');
    if (!button) return;
    clearTimeout(menuHover);
    const index = Number(button.dataset.menuItem);
    const item = entry.items[index];
    menuHover = setTimeout(() => {
      if (!ordersMenus.includes(entry)) return;
      if (item && item.sub && !button.disabled) openSubmenu(entry, index, button);
      else closeDeeper(entry);
    }, 160);
  });
}

async function openSubmenu(entry, index, button) {
  if (entry.open === index) return;
  closeDeeper(entry);
  entry.open = index;
  button.classList.add('open');
  const root = ordersMenu;
  const result = await ordersCall('menu', { menu: root.id, path: [...entry.path, index] });
  if (!result || ordersMenu !== root || entry.open !== index || ordersMenus[ordersMenus.length - 1] !== entry) return;
  if (!result.ok) {
    showToast(result.text || "Couldn't open the menu", true);
    return;
  }
  addMenuLevel([...entry.path, index], result.items, button);
}

function closeDeeper(entry) {
  const level = ordersMenus.indexOf(entry);
  if (level < 0) return;
  for (const deeper of ordersMenus.splice(level + 1)) deeper.el.remove();
  entry.open = null;
  for (const button of entry.el.querySelectorAll('.open')) button.classList.remove('open');
}

function hideOrdersMenu() {
  clearTimeout(menuHover);
  for (const level of ordersMenus) level.el.remove();
  ordersMenus = [];
  ordersMenu = null;
}

window.addEventListener('mousedown', event => {
  if (ordersMenu && !ordersMenus.some(level => level.el.contains(event.target)) && !event.target.closest('[data-orders-menu]')) hideOrdersMenu();
});

ordersPanel.addEventListener('click', event => {
  if (event.target.closest('[data-orders-close]')) {
    setOrdersOpen(false);
    return;
  }
  const tab = event.target.closest('[data-orders-tab]');
  if (tab) {
    ordersTab = tab.dataset.ordersTab;
    setGhost(null);
    renderOrders();
    return;
  }
  const menuButton = event.target.closest('[data-orders-menu]');
  if (menuButton) {
    if (ordersMenu && ordersMenu.anchor === menuButton) hideOrdersMenu();
    else openOrdersMenu(menuButton.dataset.ordersMenu, menuButton);
    return;
  }
  const button = event.target.closest('[data-orders]');
  if (!button || button.disabled) return;
  const action = button.dataset.orders;
  const index = Number(button.dataset.index);
  if (action === 'move') ordersAction('move', { index, offset: Number(button.dataset.offset) });
  else if (action === 'remove') ordersAction('remove', { index });
  else if (action === 'run') ordersAction('run', { on: !(ordersView && ordersView.running) });
  else if (action === 'save') {
    const input = ordersPanel.querySelector('[data-job-name]');
    ordersAction('save', { name: input ? input.value : '' });
    jobDraft = null;
  } else if (action === 'load' || action === 'add') {
    ordersTab = 'orders';
    ordersAction(action, { name: button.dataset.name });
  } else if (action === 'delete') ordersAction('delete', { name: button.dataset.name });
  else ordersAction(action);
});

ordersPanel.addEventListener('change', event => {
  const target = event.target;
  if (target.dataset.ordersToggle) {
    ordersAction(target.dataset.ordersToggle, { on: target.checked });
  } else if (target.dataset.ordersSet) {
    ordersAction(target.dataset.ordersSet, { index: Number(target.dataset.index), value: target.value });
    target.blur();
  } else if (target.dataset.ordersTime) {
    const index = Number(target.dataset.index);
    const hour = ordersPanel.querySelector(`[data-orders-time="hour"][data-index="${index}"]`);
    const minute = ordersPanel.querySelector(`[data-orders-time="minute"][data-index="${index}"]`);
    ordersAction('time', { index, value: Number(hour.value) * 60 + Number(minute.value) });
    target.blur();
  } else if (target.dataset.ordersSmart !== undefined) {
    ordersAction('smart', { index: Number(target.dataset.ordersSmart), on: target.checked });
  }
});

ordersPanel.addEventListener('mouseover', event => {
  const row = ordersTab === 'orders' ? event.target.closest('[data-row]') : null;
  setGhost(row ? Number(row.dataset.row) : null);
});

ordersPanel.addEventListener('mouseleave', () => setGhost(null));

ordersPanel.addEventListener('input', event => {
  if (event.target.dataset.jobName !== undefined) jobDraft = event.target.value;
});

function trackMenu(px, py, hit) {
  const x = wx(px);
  const z = wz(py);
  const here = { label: 'Teleport here', sub: 'Or Ctrl+click the map', run: () => teleport({ point: `${x.toFixed(2)},${z.toFixed(2)}` }) };
  if (hit && hit.type === 'fusee') {
    showMenu(px, py, 'Fusee', [{ label: 'Extinguish fusee', run: () => fuseeAction('remove', hit.fusee[0], hit.fusee[1], hit.fusee[2]) }, here]);
    return;
  }
  if (hit && hit.type === 'car') {
    carMenu(px, py, hit);
    return;
  }
  if (!hit || hit.type !== 'track') {
    showMenu(px, py, 'Map', [here]);
    return;
  }
  const near = nearestFusee(x, z, 20);
  const train = selected && locoById.has(selected) ? selected : null;
  showMenu(px, py, 'Track', [
    train ? sendItem(train, x, z) : { label: 'Send a train here', sub: 'Select a train first', disabled: true },
    near
      ? { label: 'Extinguish fusee', run: () => fuseeAction('remove', near[0], near[1], near[2]) }
      : { label: 'Place fusee', sub: 'The engineer stops here', run: () => fuseeAction('place', x, z) },
    here]);
}

function capturing(train) {
  return !!(train && ordersOpen && ordersTrain === train && ordersView && ordersView.rows);
}

function sendItem(train, x, z) {
  return capturing(train)
    ? { label: `Send ${locoName(train)} here`, sub: 'Added to its orders', run: () => ordersAction('waypoint', { point: `${x.toFixed(2)},${z.toFixed(2)}` }) }
    : { label: `Send ${locoName(train)} here`, sub: 'The engineer drives the train there', run: () => sendHere(train, x, z) };
}

function carMenu(px, py, hit) {
  const car = hit.car;
  const name = `${car[6] || ''} ${car[7] || ''}`.trim() || 'Car';
  const train = selected && locoById.has(selected) ? selected : null;
  const trainCar = train ? curr.get(train) : null;
  const own = !!(trainCar && trainCar[12] && hit.car[12] === trainCar[12]);
  let item;
  if (!train) item = { label: `Couple to ${name}`, sub: 'Select a train first', disabled: true };
  else if (own || hit.id === train) item = { label: `Couple to ${name}`, sub: 'Already in its train', disabled: true };
  else if (capturing(train)) item = { label: `Couple ${locoName(train)} to ${name}`, sub: 'Added to its orders', run: () => coupleTo(train, hit.id, true) };
  else item = { label: `Couple ${locoName(train)} to ${name}`, sub: 'The engineer couples to it, then air and handbrakes', run: () => coupleTo(train, hit.id) };
  const items = [item];
  const track = hitTrack(px, py);
  if (train && track && track.type === 'track') items.push(sendItem(train, wx(px), wz(py)));
  items.push({ label: `Teleport to ${name}`, sub: 'Move your camera to this car', run: () => teleport({ car: hit.id }) });
  showMenu(px, py, name, items);
}

async function teleport(target) {
  try {
    const headers = {};
    if (target.car) headers['X-Map-Plus-Car'] = target.car;
    if (target.point) headers['X-Map-Plus-Point'] = target.point;
    const response = await fetch(API + 'teleport', { cache: 'no-store', headers });
    const result = await response.json();
    showToast(result.text || (result.ok ? 'Teleported' : "That didn't work"), !result.ok);
  } catch (error) {
    showToast("Couldn't reach the game", true);
  }
}

async function coupleTo(train, car, queue) {
  try {
    const headers = { 'X-Map-Plus-Train': train, 'X-Map-Plus-Car': car };
    if (queue) headers['X-Map-Plus-Queue'] = '1';
    const response = await fetch(API + 'couple', { cache: 'no-store', headers });
    const result = await response.json();
    if (queue && ordersOpen) refreshOrders();
    showToast(result.text || (result.ok ? 'Coupling' : "That didn't work"), !result.ok);
  } catch (error) {
    showToast("Couldn't reach the game", true);
  }
}

function signalPoint(signal) {
  return { x: sx(signal[1]) + signal[4] * 9, y: sy(signal[2]) + signal[3] * 9 };
}

function drawSignals(g) {
  if (view.scale < SIGNAL_SCALE) return;
  const size = 11;
  for (const signal of signals) {
    const { x, y } = signalPoint(signal);
    if (x < -20 || y < -20 || x > width + 20 || y > height + 20) continue;
    const ux = signal[3];
    const uy = -signal[4];
    g.beginPath();
    g.moveTo(x + ux * size, y + uy * size);
    g.lineTo(x - ux * size * 0.6 - uy * size * 0.7, y - uy * size * 0.6 + ux * size * 0.7);
    g.lineTo(x - ux * size * 0.6 + uy * size * 0.7, y - uy * size * 0.6 - ux * size * 0.7);
    g.closePath();
    g.fillStyle = ASPECT_COLORS[signal[5]] || '#FFFFFF';
    g.strokeStyle = INK;
    g.lineWidth = 2;
    g.fill();
    g.stroke();
  }
}

function drawTrails(g, t, now, group) {
  g.lineCap = 'round';
  for (const [id, trail] of trails) {
    if (!curr.has(trail.car)) continue;
    const pose = carPose(trail.car, t);
    const cx = (pose.fx + pose.rx) / 2;
    const cz = (pose.fz + pose.rz) / 2;
    const tail = { x: cx - trail.ux * trail.length / 2, z: cz - trail.uz * trail.length / 2, t: now };
    const points = trail.points.filter(point => (point.x - tail.x) * trail.ux + (point.z - tail.z) * trail.uz < -0.5);
    if (!points.length) continue;
    points.push(tail);
    const color = id === group ? '140, 230, 255' : '255, 236, 190';
    const full = Math.max(4, CAR_WIDTH * view.scale * 0.5);
    for (let i = 1; i < points.length; i++) {
      const life = clamp(1 - (now - points[i].t) / TRAIL_SECONDS, 0, 1);
      g.strokeStyle = `rgba(${color}, ${0.55 * life})`;
      g.lineWidth = full * (0.3 + 0.7 * life);
      g.beginPath();
      g.moveTo(sx(points[i - 1].x), sy(points[i - 1].z));
      g.lineTo(sx(points[i].x), sy(points[i].z));
      g.stroke();
    }
  }
}

function drawHeadlights(g, t) {
  for (const [id, car] of curr) {
    if (car[8] !== 1) continue;
    const loco = locoById.get(id);
    if (!loco || loco.s < 1) continue;
    const shape = carShape(carPose(id, t));
    if (shape.x < -200 || shape.y < -200 || shape.x > width + 200 || shape.y > height + 200) continue;
    const reach = clamp(45 * view.scale, 18, 150);
    const spread = 0.36;
    const tip = shape.long / 2;
    g.save();
    g.translate(shape.x, shape.y);
    g.rotate(shape.angle);
    g.scale(heading.get(id) || 1, 1);
    const glow = g.createRadialGradient(tip, 0, 0, tip, 0, reach);
    glow.addColorStop(0, 'rgba(255, 238, 170, 0.6)');
    glow.addColorStop(1, 'rgba(255, 238, 170, 0)');
    g.fillStyle = glow;
    g.beginPath();
    g.moveTo(tip, 0);
    g.arc(tip, 0, reach, -spread, spread);
    g.closePath();
    g.fill();
    g.restore();
  }
}

function highlights(group) {
  const chosen = new Map();
  for (const car of groups.get(group) || []) chosen.set(car[0], ROUTE_COLOR);
  if (selected && curr.has(selected)) chosen.set(selected, ROUTE_COLOR);
  const industry = selectedPlace();
  if (industry) {
    for (const [id] of industry.co) chosen.set(id, COMING_COLOR);
    for (const [id] of industry.cs) chosen.set(id, SPOTTED_COLOR);
  }
  if (ping && ping.until > performance.now() && curr.has(ping.id)) chosen.set(ping.id, '#FFFFFF');
  return chosen;
}

function selectedPlace() {
  return selectedIndustry ? places.industries.find(place => place.i === selectedIndustry) || null : null;
}

function drawCars(g, t, time, chosen) {
  const pulse = 1 + 0.35 * Math.sin(((time / 1000) % 1) * Math.PI);
  const badges = view.scale < SNAKE_SCALE ? drawSnakes(g, t, chosen) : drawBodies(g, t, chosen);
  for (const { car, shape } of badges) {
    const radius = Math.max(4, shape.wide * 0.75) * (car[8] === 1 && car[11] >= 3 ? pulse : 1);
    g.beginPath();
    g.arc(shape.x + shape.ux * (shape.long / 2), shape.y + shape.uy * (shape.long / 2), radius, 0, Math.PI * 2);
    g.fillStyle = car[10];
    g.strokeStyle = INK;
    g.lineWidth = 2;
    g.fill();
    g.stroke();
  }
}

function drawSnakes(g, t, chosen) {
  const items = [];
  for (const id of curr.keys()) {
    if (!showCar(curr.get(id))) continue;
    const pose = carPose(id, t);
    const shape = carShape(pose);
    if (shape.x < -60 || shape.y < -60 || shape.x > width + 60 || shape.y > height + 60) continue;
    const half = shape.long / 2;
    items.push({
      id, car: pose.car, shape,
      ax: shape.x - shape.ux * half, ay: shape.y - shape.uy * half,
      bx: shape.x + shape.ux * half, by: shape.y + shape.uy * half
    });
  }
  const segment = item => {
    g.beginPath();
    g.moveTo(item.ax, item.ay);
    g.lineTo(item.bx, item.by);
    g.stroke();
  };
  g.lineCap = 'round';
  for (const item of items) {
    const color = chosen.get(item.id);
    g.shadowColor = color || ROUTE_COLOR;
    g.shadowBlur = color ? 10 : 0;
    g.strokeStyle = color || 'rgba(10, 9, 7, 0.95)';
    g.lineWidth = item.shape.wide + (color ? 5 : 3);
    segment(item);
  }
  g.shadowBlur = 0;
  for (const item of items) {
    g.strokeStyle = item.car[8] === 1 ? '#F6F1E6' : item.car[9] || '#FFFFFF';
    g.lineWidth = item.shape.wide;
    segment(item);
  }
  const badges = [];
  for (const item of items) {
    if (layers.alerts && item.car[10]) badges.push(item);
    if (item.car[8] !== 1) continue;
    const { ux, uy, wide } = item.shape;
    const tipX = item.bx + ux * wide * 0.9;
    const tipY = item.by + uy * wide * 0.9;
    const baseX = item.bx - ux * wide * 0.2;
    const baseY = item.by - uy * wide * 0.2;
    const side = wide * 0.6 + 1;
    g.beginPath();
    g.moveTo(tipX, tipY);
    g.lineTo(baseX - uy * side, baseY + ux * side);
    g.lineTo(baseX + uy * side, baseY - ux * side);
    g.closePath();
    g.fillStyle = '#F6F1E6';
    g.strokeStyle = chosen.get(item.id) || INK;
    g.lineWidth = 1.5;
    g.fill();
    g.stroke();
  }
  drawMarkers(g, t, chosen);
  return badges;
}

function drawMarkers(g, t, chosen) {
  for (const [id, list] of groups) {
    if (!locoById.has(id) || !curr.has(id) || !list.length) continue;
    const first = carShape(carPose(list[0][0], t));
    const last = carShape(carPose(list[list.length - 1][0], t));
    const loco = carShape(carPose(id, t));
    let ux = last.x - first.x;
    let uy = last.y - first.y;
    const length = Math.hypot(ux, uy);
    if (length + (first.long + last.long) / 2 >= MARKER_PIXELS) continue;
    const cx = (first.x + last.x) / 2;
    const cy = (first.y + last.y) / 2;
    if (cx < -40 || cy < -40 || cx > width + 40 || cy > height + 40) continue;
    if (length < 0.5) {
      ux = loco.ux;
      uy = loco.uy;
    } else {
      ux /= length;
      uy /= length;
    }
    if ((loco.x - cx) * ux + (loco.y - cy) * uy < 0) {
      ux = -ux;
      uy = -uy;
    }
    const half = MARKER_PIXELS / 2;
    const w = 9;
    const color = chosen.get(id) || list.map(car => chosen.get(car[0])).find(Boolean);
    g.lineCap = 'round';
    g.beginPath();
    g.moveTo(cx - ux * half, cy - uy * half);
    g.lineTo(cx + ux * half, cy + uy * half);
    g.shadowColor = color || ROUTE_COLOR;
    g.shadowBlur = color ? 12 : 0;
    g.strokeStyle = color || 'rgba(10, 9, 7, 0.95)';
    g.lineWidth = w + (color ? 5 : 3);
    g.stroke();
    g.shadowBlur = 0;
    g.strokeStyle = '#F6F1E6';
    g.lineWidth = w;
    g.stroke();
    const tipX = cx + ux * (half + w * 1.1);
    const tipY = cy + uy * (half + w * 1.1);
    const baseX = cx + ux * (half - w * 0.1);
    const baseY = cy + uy * (half - w * 0.1);
    const side = w * 0.75;
    g.beginPath();
    g.moveTo(tipX, tipY);
    g.lineTo(baseX - uy * side, baseY + ux * side);
    g.lineTo(baseX + uy * side, baseY - ux * side);
    g.closePath();
    g.fillStyle = color || '#F6F1E6';
    g.strokeStyle = INK;
    g.lineWidth = 1.5;
    g.fill();
    g.stroke();
  }
}

function drawBodies(g, t, chosen) {
  const badges = [];
  for (const id of curr.keys()) {
    if (!showCar(curr.get(id))) continue;
    const pose = carPose(id, t);
    const car = pose.car;
    const shape = carShape(pose);
    if (shape.x < -80 || shape.y < -80 || shape.x > width + 80 || shape.y > height + 80) continue;
    const half = shape.long / 2;
    const side = shape.wide / 2;
    const color = chosen.get(id);
    g.save();
    g.translate(shape.x, shape.y);
    g.rotate(shape.angle);
    if (color) {
      g.shadowColor = color;
      g.shadowBlur = 12;
    }
    g.beginPath();
    if (car[8] === 1) {
      const nose = Math.min(shape.wide * 0.9, shape.long * 0.35);
      g.moveTo(-half, -side);
      g.lineTo(half - nose, -side);
      g.lineTo(half, 0);
      g.lineTo(half - nose, side);
      g.lineTo(-half, side);
      g.closePath();
      g.fillStyle = '#F6F1E6';
    } else {
      roundRect(g, -half, -side, shape.long, shape.wide, Math.min(side * 0.6, 4));
      g.fillStyle = car[9] || '#FFFFFF';
    }
    g.fill();
    g.shadowBlur = 0;
    g.lineWidth = color ? 2 : 1;
    g.strokeStyle = color || 'rgba(0, 0, 0, 0.75)';
    g.stroke();
    if (car[8] === 1 && shape.wide >= 6) {
      g.fillStyle = 'rgba(30, 26, 20, 0.55)';
      g.fillRect(-half + shape.long * 0.12, -side * 0.45, shape.long * 0.22, side * 0.9);
    } else if (car[8] !== 1 && shape.wide >= 7) {
      g.fillStyle = 'rgba(255, 255, 255, 0.16)';
      g.fillRect(-half + 2, -side * 0.3, shape.long - 4, side * 0.6);
    }
    if (layers.labels && car[8] !== 1) drawCarLabel(g, car, shape);
    g.restore();
    if (layers.alerts && car[10]) badges.push({ car, shape });
  }
  return badges;
}

function drawCarLabel(g, car, shape) {
  if (shape.wide < 8) return;
  const text = car[8] === 2 ? car[7] : `${car[6]} ${car[7]}`;
  g.font = `600 ${Math.min(shape.wide * 0.6, 12)}px ${FONT}`;
  if (g.measureText(text).width + 6 > shape.long) return;
  if (Math.abs(shape.angle) > Math.PI / 2) g.rotate(Math.PI);
  g.fillStyle = '#111111';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(text, 0, 0.5);
}

function overlaps(a, list) {
  return list.some(b => a.x < b.x + b.w + 3 && a.x + a.w + 3 > b.x && a.y < b.y + b.h + 3 && a.y + a.h + 3 > b.y);
}

function drawTags(g, t, group, chosen) {
  const items = [];
  for (const loco of locos) {
    if (!groups.has(loco.i) || !curr.has(loco.i)) continue;
    const shape = carShape(carPose(loco.i, t));
    if (shape.x < -150 || shape.y < -150 || shape.x > width + 150 || shape.y > height + 150) continue;
    const accent = loco.i === group ? ROUTE_COLOR : (groups.get(loco.i) || []).map(car => chosen.get(car[0])).find(Boolean) || null;
    items.push({ loco, shape, accent, priority: loco.i === group ? 0 : accent ? 1 : loco.s >= 1 ? 2 : 3 });
  }
  items.sort((a, b) => a.priority - b.priority || b.loco.s - a.loco.s);
  const marked = view.scale < SNAKE_SCALE;
  const reach = MARKER_PIXELS / 2 + 4;
  const placed = marked ? items.map(({ shape }) => ({ x: shape.x - reach, y: shape.y - reach, w: reach * 2, h: reach * 2 })) : [];
  tagRects = [];
  g.textBaseline = 'middle';
  g.textAlign = 'left';
  for (const { loco, shape, priority, accent } of items) {
    const isSelected = priority === 0;
    const speed = `${loco.s} mph`;
    g.font = `bold 12px ${FONT}`;
    const nameWidth = g.measureText(loco.n).width;
    g.font = `11px ${FONT}`;
    const speedWidth = g.measureText(speed).width;
    const w = 26 + nameWidth + 8 + speedWidth + 10;
    const h = 22;
    const d = marked ? reach + 6 : Math.max(shape.wide, 8) + 10;
    const options = [];
    for (const ring of [1, 2.4]) {
      const r = d * ring;
      options.push([0, -(r + h / 2)], [0, r + h / 2], [r + w / 2, 0], [-(r + w / 2), 0],
        [r * 0.7 + w / 2, -(r * 0.7 + h / 2)], [-(r * 0.7 + w / 2), -(r * 0.7 + h / 2)],
        [r * 0.7 + w / 2, r * 0.7 + h / 2], [-(r * 0.7 + w / 2), r * 0.7 + h / 2]);
    }
    let rect = null;
    for (const [ox, oy] of options) {
      const candidate = { x: shape.x + ox - w / 2, y: shape.y + oy - h / 2, w, h };
      if (candidate.x < 4 || candidate.y < 8 || candidate.x + w > width - 4 || candidate.y + h > height - 4) continue;
      if (!overlaps(candidate, placed)) {
        rect = candidate;
        break;
      }
    }
    if (!rect) {
      if (!accent) continue;
      rect = { x: shape.x - w / 2, y: shape.y - d - h, w, h };
    }
    placed.push(rect);
    tagRects.push({ rect, id: loco.i });
    const cy = rect.y + h / 2;
    const nearX = clamp(shape.x, rect.x, rect.x + w);
    const nearY = clamp(shape.y, rect.y, rect.y + h);
    if (Math.hypot(nearX - shape.x, nearY - shape.y) > d * 0.6) {
      g.strokeStyle = accent ? rgba(accent, 0.8) : 'rgba(237, 231, 218, 0.55)';
      g.lineWidth = 1.2;
      g.beginPath();
      g.moveTo(shape.x, shape.y);
      g.lineTo(nearX, nearY);
      g.stroke();
    }
    g.beginPath();
    roundRect(g, rect.x, rect.y, w, h, h / 2);
    g.fillStyle = 'rgba(22, 19, 14, 0.92)';
    g.fill();
    g.lineWidth = accent ? 1.6 : 1;
    g.strokeStyle = accent || 'rgba(237, 231, 218, 0.28)';
    g.stroke();
    g.beginPath();
    g.arc(rect.x + 13, cy, 4, 0, Math.PI * 2);
    g.fillStyle = (MODES[loco.m] || MODES[0]).color;
    g.fill();
    g.font = `bold 12px ${FONT}`;
    g.fillStyle = isSelected ? ROUTE_COLOR : '#F4EFE4';
    g.fillText(loco.n, rect.x + 23, cy + 0.5);
    g.font = `11px ${FONT}`;
    g.fillStyle = '#A2977F';
    g.fillText(speed, rect.x + 23 + nameWidth + 8, cy + 0.5);
  }
}

function updateLegend() {
  const show = layers.classes;
  if (show && legend.classList.contains('hidden')) {
    legend.innerHTML = 'Track class' + CLASS_COLORS.map((color, index) => `<b style="color:${color}">${index + 1}</b>`).join('');
  }
  legend.classList.toggle('hidden', !show);
}

function hitTag(px, py) {
  for (let i = tagRects.length - 1; i >= 0; i--) {
    const { rect, id } = tagRects[i];
    if (px >= rect.x && px <= rect.x + rect.w && py >= rect.y && py <= rect.y + rect.h) return id;
  }
  return null;
}

function hitTest(px, py) {
  if (highlightArrow && Math.hypot(px - highlightArrow.x, py - highlightArrow.y) <= highlightArrow.r) return { type: 'highlight' };
  const tag = hitTag(px, py);
  if (tag && curr.has(tag)) return { type: 'car', id: tag, car: curr.get(tag) };
  for (let i = orderHits.length - 1; i >= 0; i--) {
    const spot = orderHits[i];
    if (Math.hypot(px - spot.x, py - spot.y) <= spot.r + 3) return { type: 'orders', group: spot.group };
  }
  const t = mix();
  let best = null;
  let bestDistance = Infinity;
  for (const id of curr.keys()) {
    if (!showCar(curr.get(id))) continue;
    const shape = carShape(carPose(id, t));
    const dx = px - shape.x;
    const dy = py - shape.y;
    const along = dx * shape.ux + dy * shape.uy;
    const across = -dx * shape.uy + dy * shape.ux;
    if (Math.abs(along) > shape.long / 2 + 3 || Math.abs(across) > shape.wide / 2 + 4) continue;
    const distance = Math.abs(across);
    if (distance < bestDistance) {
      bestDistance = distance;
      best = { type: 'car', id, car: curr.get(id) };
    }
  }
  if (best) return best;
  for (let i = placeHits.length - 1; i >= 0; i--) {
    const spot = placeHits[i];
    if (Math.hypot(px - spot.x, py - spot.y) <= spot.r + 2) return { type: spot.type, item: spot.item };
  }
  if (layers.fusees) {
    for (const fusee of fusees) {
      if (Math.hypot(px - sx(fusee[0]), py - sy(fusee[1])) < 18) return { type: 'fusee', fusee };
    }
  }
  if (layers.signals && view.scale >= SIGNAL_SCALE) {
    for (const signal of signals) {
      const point = signalPoint(signal);
      if (Math.hypot(px - point.x, py - point.y) < 10) return { type: 'signal', signal };
    }
  }
  return hitTrack(px, py);
}

function distanceToPiece(x, z, ax, az, bx, bz) {
  const dx = bx - ax;
  const dz = bz - az;
  const length = dx * dx + dz * dz;
  const k = length > 0 ? clamp(((x - ax) * dx + (z - az) * dz) / length, 0, 1) : 0;
  return Math.hypot(x - ax - dx * k, z - az - dz * k);
}

function hitTrack(px, py) {
  if (!paths) return null;
  const x = wx(px);
  const z = wz(py);
  const tolerance = 7 / view.scale;
  let best = -1;
  let bestDistance = tolerance;
  for (let i = 0; i < segments.length; i++) {
    const segment = segments[i];
    if (x < segment.minX - tolerance || x > segment.maxX + tolerance || z < segment.minZ - tolerance || z > segment.maxZ + tolerance) continue;
    const { xs, zs, n } = samplesOf(segment);
    for (let k = 1; k <= n; k++) {
      const distance = distanceToPiece(x, z, xs[k - 1], zs[k - 1], xs[k], zs[k]);
      if (distance < bestDistance) {
        bestDistance = distance;
        best = i;
      }
    }
  }
  return best >= 0 ? { type: 'track', index: best } : null;
}

function nearestTown(x, z) {
  let best = null;
  let bestDistance = Infinity;
  for (const town of (track && track.towns) || []) {
    const distance = Math.hypot(town.x - x, town.z - z);
    if (distance < bestDistance) {
      bestDistance = distance;
      best = town;
    }
  }
  return best ? (bestDistance < Math.max(best.r, 300) * 1.5 ? best.n : `near ${best.n}`) : '';
}

function trackHtml(index) {
  const segment = segments[index];
  if (!segment) return '';
  const info = segmentInfo && track && segmentInfo.sv === track.version ? segmentInfo.s[index] : null;
  const place = info && info[4] >= 0 ? segmentInfo.pl[info[4]] : null;
  const kind = place ? PLACE_KINDS[place[3]] || 'Industry track' : segment.kind === 1 ? 'Mainline' : (segment.flags & 4) ? 'Yard track' : 'Track';
  const title = place ? place[0] : kind;
  const meta = place ? [place[1], kind, place[2]].filter(Boolean).join(' · ') : nearestTown((segment.p[0] + segment.p[6]) / 2, (segment.p[1] + segment.p[7]) / 2);
  const lines = [];
  if (segment.state === 2) lines.push('Not built yet');
  else if (segment.state === 1) lines.push('Not available');
  else {
    const trackClass = clamp(segment.cls || 1, 1, CLASS_COLORS.length);
    lines.push(`<span style="color:${CLASS_COLORS[trackClass - 1]}">Class ${trackClass}</span>, up to <b>${MAX_SPEEDS[trackClass - 1]} mph</b>`);
    if (info) {
      lines.push(`Speed limit <b>${info[0]} mph</b>`);
      if (info[2] >= 0) lines.push(`Condition <b>${info[2]}%</b>`);
      if (info[1] > 0) lines.push(`Built for class <b>${info[1]}</b>`);
      lines.push(`Grade <b>${Number(info[3]).toFixed(1)}%</b>`);
    }
  }
  const features = [segment.flags & 1 ? 'Bridge' : null, segment.flags & 2 ? 'Tunnel' : null].filter(Boolean);
  if (features.length) lines.push(features.join(', '));
  return `<div class="title">${escapeHtml(title)}</div>${meta ? `<div class="meta">${escapeHtml(meta)}</div>` : ''}${lines.map(line => `<div class="field">${line}</div>`).join('')}`;
}

function drawHoverTrack(g) {
  const segment = hoverSegment >= 0 ? segments[hoverSegment] : null;
  if (!segment) return;
  const path = new Path2D();
  curveTo(path, segment.p);
  worldTransform(g);
  g.lineCap = 'round';
  strokeWorld(g, path, 'rgba(255, 255, 255, 0.35)', Math.max(trackWidth() + 7, 4.2 * view.scale + 4));
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function richText(text) {
  const source = String(text || '');
  let html = '';
  let last = 0;
  let open = 0;
  const pattern = /<(\/?)(color|b|alpha)(?:=([^>]+))?>|<[^>]+>/g;
  for (const match of source.matchAll(pattern)) {
    html += escapeHtml(source.slice(last, match.index));
    last = match.index + match[0].length;
    const pie = match[0].startsWith('<sprite') ? /name=Pie(\d+)/.exec(match[0]) : null;
    if (pie) {
      html += `<i class="pie" style="--p:${Math.round(Number(pie[1]) / 16 * 100)}"></i>`;
    } else if (match[2] === 'color' && !match[1]) {
      html += `<span style="color:${escapeHtml((match[3] || '').replace(/"/g, ''))}">`;
      open++;
    } else if (match[2] === 'color' && open > 0) {
      html += '</span>';
      open--;
    } else if (match[2] === 'b') {
      html += match[1] ? '</b>' : '<b>';
    } else if (match[2] === 'alpha' && !match[1]) {
      const alpha = parseInt((match[3] || '').replace('#', ''), 16);
      html += `<span style="opacity:${Number.isFinite(alpha) ? (alpha / 255).toFixed(2) : 1}">`;
      open++;
    }
  }
  html += escapeHtml(source.slice(last));
  return html + '</span>'.repeat(open);
}

function showTooltip(hit, px, py) {
  if (!hit) {
    tooltip.classList.add('hidden');
    return;
  }
  if (hit.type === 'place' || hit.type === 'fuel' || hit.type === 'track') {
    tooltip.innerHTML = hit.type === 'place' ? placeHtml(hit.item) : hit.type === 'fuel' ? fuelHtml(hit.item) : trackHtml(hit.index);
    placeTooltip(px, py);
    return;
  }
  if (hit.type === 'highlight') {
    tooltip.innerHTML = `<div class="title">${escapeHtml(highlightName())}</div><div class="meta">Highlighted in the game</div><div class="field">Click to go there</div>`;
    placeTooltip(px, py);
    return;
  }
  if (hit.type === 'orders') {
    tooltip.innerHTML = ordersHtml(hit.group);
    placeTooltip(px, py);
    return;
  }
  if (hit.type === 'fusee') {
    tooltip.innerHTML = '<div class="title">Fusee</div><div class="body">The engineer stops here.</div><div class="meta">Right-click to put it out</div>';
    placeTooltip(px, py);
    return;
  }
  let title;
  let body;
  if (hit.type === 'car') {
    const info = details.cars[hit.id];
    title = info ? info.t : `${hit.car[6]} ${hit.car[7]}`;
    body = info ? info.b : '';
    const loco = locoById.get(hit.id);
    if (loco) {
      const mode = MODES[loco.m] || MODES[0];
      body = `<color=${mode.color}>${loco.m ? 'AE ' : ''}${mode.name}</color>, ${loco.s} mph` + (body ? `\n${body}` : '');
    }
  } else {
    const [id, , , , , aspect] = hit.signal;
    title = details.signals[id] || 'Signal';
    body = `<color=${ASPECT_COLORS[aspect]}>${ASPECT_NAMES[aspect] || ''}</color>`;
  }
  tooltip.innerHTML = `<div class="title">${richText(title)}</div><div class="body">${richText(body)}</div>`;
  placeTooltip(px, py);
}

function placeTooltip(px, py) {
  tooltip.classList.remove('hidden');
  tooltip.style.left = `${Math.max(8, Math.min(px + 16, width - tooltip.offsetWidth - 8))}px`;
  tooltip.style.top = `${Math.max(8, Math.min(py + 16, height - tooltip.offsetHeight - 8))}px`;
}

function placeHtml(place) {
  const category = CATEGORIES[place.k] || CATEGORIES.freight;
  const parts = [`<div class="title">${escapeHtml(place.n)}</div>`, `<div class="meta">${escapeHtml(category.label)} · ${escapeHtml(place.t)}</div>`];
  for (const track of place.tr) {
    const counts = [];
    if (track.s) counts.push(`${track.s} spotted`);
    if (track.o) counts.push(`${track.o} on the way`);
    parts.push(`<div class="field">${escapeHtml(track.n)}: <b>${counts.length ? counts.join(', ') : 'no cars'}</b></div>`);
  }
  if (place.ct !== null && place.ct !== undefined) parts.push(`<div class="field">Contract: <b>tier ${place.ct}</b></div>`);
  if (place.px) {
    parts.push('<div class="section">Passengers</div>');
    parts.push(`<div class="field">Waiting: <b>${place.px.total}</b></div>`);
    for (const [name, count] of place.px.d) parts.push(`<div class="field">To ${escapeHtml(name)}: <b>${count}</b></div>`);
    parts.push(`<div class="field">Last train: <b>${place.px.h === null ? 'not yet' : ago(place.px.h)}</b></div>`);
    if (place.px.flag) parts.push('<div class="field">Flag stop</div>');
  }
  for (const section of place.sec) {
    parts.push(`<div class="section">${escapeHtml(section.n)}</div>`);
    for (const [label, text] of section.f) {
      parts.push(`<div class="field">${label ? `${escapeHtml(label)}: ` : ''}<b>${richText(text)}</b></div>`);
    }
  }
  for (const warning of place.w) parts.push(`<div class="warn">${richText(warning)}</div>`);
  return parts.join('');
}

function fuelHtml(fuel) {
  const category = CATEGORIES[fuel.k] || CATEGORIES.water;
  const stock = fuel.s === null ? 'Unlimited' : fuel.e ? '<span class="warn">Out of stock</span>' : `In stock: <b>${escapeHtml(fuel.s)}</b>`;
  return `<div class="title">${escapeHtml(fuel.n)}</div><div class="meta">${escapeHtml(category.label)}${fuel.t ? ` · ${escapeHtml(fuel.t)}` : ''}</div><div class="field">${stock}</div>`;
}

function busy(item) {
  return item.type === 'place' ? item.item.tr.reduce((sum, track) => sum + track.s + track.o, 0) : 0;
}

function drawPlaces(g) {
  placeHits = [];
  if (!layers.places || view.scale < PLACE_SCALE) return;
  const items = [];
  const size = view.scale < SMALL_PLACE_SCALE ? 8 : 11;
  const hidden = new Set(layers.hiddenKinds || []);
  for (const place of places.industries) if (!hidden.has(place.k)) items.push({ type: 'place', item: place, kind: place.k, x: sx(place.x), y: sy(place.z), r: size });
  if (view.scale >= FUEL_SCALE && !hidden.has('fuel')) {
    for (const fuel of places.fuel) items.push({ type: 'fuel', item: fuel, kind: fuel.k, x: sx(fuel.x), y: sy(fuel.z), r: 8 });
  }
  const visible = items.filter(item => item.x > -40 && item.y > -40 && item.x < width + 40 && item.y < height + 40);
  visible.sort((a, b) => b.r - a.r || busy(b) - busy(a));
  const placed = [];
  const names = [];
  for (const item of visible) {
    const free = (x, y) => placed.every(other => Math.hypot(other.x - x, other.y - y) > other.r + item.r + 2);
    let cx = item.x;
    let cy = item.y;
    if (!free(cx, cy)) {
      let found = false;
      for (const ring of [1, 2]) {
        for (let k = 0; k < 8 && !found; k++) {
          const angle = -Math.PI / 2 + k * Math.PI / 4;
          const distance = (item.r * 2 + 3) * ring;
          const x = item.x + Math.cos(angle) * distance;
          const y = item.y + Math.sin(angle) * distance;
          if (free(x, y)) {
            cx = x;
            cy = y;
            found = true;
          }
        }
        if (found) break;
      }
      if (!found) continue;
    }
    placed.push({ x: cx, y: cy, r: item.r });
    if (cx !== item.x || cy !== item.y) {
      g.strokeStyle = 'rgba(237, 231, 218, 0.6)';
      g.lineWidth = 1.2;
      g.beginPath();
      g.moveTo(item.x, item.y);
      g.lineTo(cx, cy);
      g.stroke();
      g.beginPath();
      g.arc(item.x, item.y, 2.5, 0, Math.PI * 2);
      g.fillStyle = '#EDE7DA';
      g.fill();
    }
    if ((item.type === 'place' && item.item.i === selectedIndustry) || (item.type === 'fuel' && item.item.i === selectedFuel)) {
      g.beginPath();
      g.arc(cx, cy, item.r + 5, 0, Math.PI * 2);
      g.strokeStyle = COMING_COLOR;
      g.lineWidth = 2.5;
      g.shadowColor = COMING_COLOR;
      g.shadowBlur = 10;
      g.stroke();
      g.shadowBlur = 0;
    }
    drawIcon(g, item.kind, cx, cy, item.r, item.type === 'fuel' && item.item.e);
    if (item.type === 'place' && view.scale >= COUNT_SCALE) drawCounts(g, item.item, cx + item.r + 3, cy);
    placeHits.push({ x: cx, y: cy, r: item.r, type: item.type, item: item.item });
    if (view.scale >= NAME_SCALE) names.push({ text: item.item.n, x: cx, y: cy + item.r + 3, small: item.type === 'fuel' });
  }
  const taken = [];
  g.textAlign = 'center';
  g.textBaseline = 'top';
  g.lineJoin = 'round';
  for (const name of names) {
    g.font = `600 ${name.small ? 10 : 11}px ${FONT}`;
    const w = g.measureText(name.text).width;
    const rect = { x: name.x - w / 2, y: name.y, w, h: name.small ? 12 : 13 };
    if (overlaps(rect, taken)) continue;
    taken.push(rect);
    g.lineWidth = 3;
    g.strokeStyle = 'rgba(16, 14, 10, 0.9)';
    g.strokeText(name.text, name.x, name.y);
    g.fillStyle = '#EDE7DA';
    g.fillText(name.text, name.x, name.y);
  }
}

function ago(hours) {
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} min ago`;
  if (hours < 48) return `${Math.round(hours)} h ago`;
  return `${Math.round(hours / 24)} days ago`;
}

function drawCounts(g, place, x, y) {
  const spotted = place.tr.reduce((sum, track) => sum + track.s, 0);
  const coming = place.tr.reduce((sum, track) => sum + track.o, 0);
  const riders = place.px ? place.px.total : 0;
  if (!spotted && !coming && !riders) return;
  g.font = `bold 11px ${FONT}`;
  const pieces = [];
  if (riders) pieces.push({ text: String(riders), color: RIDER_COLOR, person: true });
  if (spotted || coming) {
    pieces.push({ text: String(spotted), color: '#F4EFE4', gap: riders ? 7 : 0 });
    if (coming) pieces.push({ text: ` →${coming}`, color: COMING_COLOR });
  }
  let w = 10;
  for (const piece of pieces) {
    piece.width = g.measureText(piece.text).width;
    w += piece.width + (piece.person ? 9 : 0) + (piece.gap || 0);
  }
  g.beginPath();
  roundRect(g, x, y - 8, w, 16, 8);
  g.fillStyle = 'rgba(22, 19, 14, 0.9)';
  g.fill();
  g.textAlign = 'left';
  g.textBaseline = 'middle';
  let cursor = x + 5;
  for (const piece of pieces) {
    cursor += piece.gap || 0;
    if (piece.person) {
      g.fillStyle = piece.color;
      g.beginPath();
      g.arc(cursor + 3, y - 2.5, 2, 0, Math.PI * 2);
      g.fill();
      g.beginPath();
      roundRect(g, cursor, y, 6, 4.5, 2);
      g.fill();
      cursor += 9;
    }
    g.fillStyle = piece.color;
    g.fillText(piece.text, cursor, y + 0.5);
    cursor += piece.width;
  }
}

function drawIcon(g, kind, x, y, r, empty) {
  const category = CATEGORIES[kind] || CATEGORIES.freight;
  g.beginPath();
  g.arc(x, y, r + 1.5, 0, Math.PI * 2);
  g.fillStyle = 'rgba(10, 9, 7, 0.9)';
  g.fill();
  g.beginPath();
  g.arc(x, y, r, 0, Math.PI * 2);
  g.fillStyle = category.color;
  g.fill();
  g.lineWidth = 1.2;
  g.strokeStyle = empty ? '#FF4038' : 'rgba(255, 255, 255, 0.75)';
  g.stroke();
  glyph(g, kind, x, y, r);
  if (empty) {
    g.strokeStyle = '#FF4038';
    g.lineWidth = 2;
    g.beginPath();
    g.moveTo(x - r * 0.7, y + r * 0.7);
    g.lineTo(x + r * 0.7, y - r * 0.7);
    g.stroke();
  }
}

function glyph(g, kind, x, y, r) {
  const s = r * 0.58;
  g.save();
  g.translate(x, y);
  g.strokeStyle = '#FFFFFF';
  g.fillStyle = '#FFFFFF';
  g.lineWidth = Math.max(1.4, r * 0.15);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  g.beginPath();
  switch (kind) {
    case 'mine':
      g.moveTo(-s * 0.75, s * 0.85);
      g.lineTo(s * 0.45, -s * 0.35);
      g.moveTo(-s * 0.55, -s * 0.85);
      g.quadraticCurveTo(s * 0.75, -s * 0.85, s * 0.9, s * 0.45);
      g.stroke();
      break;
    case 'logging':
      g.moveTo(0, -s);
      g.lineTo(s * 0.8, s * 0.35);
      g.lineTo(-s * 0.8, s * 0.35);
      g.closePath();
      g.fill();
      g.beginPath();
      g.moveTo(0, s * 0.35);
      g.lineTo(0, s);
      g.stroke();
      break;
    case 'mill':
      g.moveTo(-s, s);
      g.lineTo(-s, -s * 0.15);
      g.lineTo(-s * 0.35, -s * 0.55);
      g.lineTo(-s * 0.35, -s * 0.15);
      g.lineTo(s * 0.3, -s * 0.55);
      g.lineTo(s * 0.3, -s * 0.15);
      g.lineTo(s * 0.55, -s * 0.15);
      g.lineTo(s * 0.55, -s);
      g.lineTo(s, -s);
      g.lineTo(s, s);
      g.closePath();
      g.fill();
      break;
    case 'freight':
      g.rect(-s * 0.8, -s * 0.65, s * 1.6, s * 1.4);
      g.moveTo(-s * 0.8, -s * 0.15);
      g.lineTo(s * 0.8, -s * 0.15);
      g.moveTo(0, -s * 0.65);
      g.lineTo(0, -s * 0.15);
      g.stroke();
      break;
    case 'oil':
    case 'water':
      g.moveTo(0, -s);
      g.bezierCurveTo(s * 0.95, s * 0.05, s * 0.75, s, 0, s);
      g.bezierCurveTo(-s * 0.75, s, -s * 0.95, s * 0.05, 0, -s);
      g.fill();
      break;
    case 'mow':
      g.moveTo(-s, -s * 0.35);
      g.lineTo(s, -s * 0.35);
      g.moveTo(-s, s * 0.35);
      g.lineTo(s, s * 0.35);
      for (const tie of [-0.6, 0, 0.6]) {
        g.moveTo(s * tie, -s * 0.85);
        g.lineTo(s * tie, s * 0.85);
      }
      g.stroke();
      break;
    case 'interchange':
      g.moveTo(-s * 0.85, -s * 0.35);
      g.lineTo(s * 0.85, -s * 0.35);
      g.lineTo(s * 0.45, -s * 0.75);
      g.moveTo(s * 0.85, s * 0.35);
      g.lineTo(-s * 0.85, s * 0.35);
      g.lineTo(-s * 0.45, s * 0.75);
      g.stroke();
      break;
    case 'station':
      g.arc(0, -s * 0.55, s * 0.32, 0, Math.PI * 2);
      g.fill();
      g.beginPath();
      roundRect(g, -s * 0.5, -s * 0.12, s, s * 1.1, s * 0.3);
      g.fill();
      break;
    case 'engine':
      g.rect(-s * 0.95, -s * 0.2, s * 1.3, s * 0.75);
      g.rect(s * 0.35, -s * 0.8, s * 0.6, s * 1.35);
      g.rect(-s * 0.7, -s * 0.7, s * 0.25, s * 0.5);
      g.fill();
      g.beginPath();
      for (const wheel of [-0.6, -0.05, 0.55]) {
        g.moveTo(s * wheel + s * 0.24, s * 0.78);
        g.arc(s * wheel, s * 0.78, s * 0.24, 0, Math.PI * 2);
      }
      g.fill();
      break;
    case 'coal':
      g.moveTo(-s * 0.9, s * 0.6);
      g.lineTo(-s * 0.5, -s * 0.2);
      g.lineTo(-s * 0.1, -s * 0.05);
      g.lineTo(s * 0.2, -s * 0.6);
      g.lineTo(s * 0.75, -s * 0.1);
      g.lineTo(s * 0.9, s * 0.6);
      g.closePath();
      g.fill();
      break;
    case 'diesel':
      g.rect(-s * 0.7, -s * 0.85, s * 0.95, s * 1.75);
      g.fill();
      g.beginPath();
      g.moveTo(s * 0.25, -s * 0.35);
      g.lineTo(s * 0.8, -s * 0.05);
      g.lineTo(s * 0.8, s * 0.6);
      g.stroke();
      break;
  }
  g.restore();
}

canvas.addEventListener('contextmenu', event => {
  event.preventDefault();
  trackMenu(event.clientX, event.clientY, hitTest(event.clientX, event.clientY));
});

window.addEventListener('mousedown', event => {
  if (!menu.classList.contains('hidden') && !menu.contains(event.target)) hideMenu();
});

window.addEventListener('wheel', () => hideMenu(), { passive: true });

canvas.addEventListener('mousedown', event => {
  if (event.button !== 0) return;
  flight = null;
  hoverSegment = -1;
  drag = { x: event.clientX, y: event.clientY, vx: view.x, vz: view.z, moved: false };
});

window.addEventListener('mousemove', event => {
  if (drag) {
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (Math.hypot(dx, dy) > 4) {
      drag.moved = true;
      canvas.classList.add('dragging');
      if (follow) setFollow(false);
    }
    if (drag.moved) {
      view.x = drag.vx - dx / view.scale;
      view.z = drag.vz + dy / view.scale;
    }
    tooltip.classList.add('hidden');
    return;
  }
  if (event.target !== canvas) {
    hoverSegment = -1;
    canvas.classList.remove('clickable');
    tooltip.classList.add('hidden');
    return;
  }
  const hit = hitTest(event.clientX, event.clientY);
  hoverSegment = hit && hit.type === 'track' ? hit.index : -1;
  canvas.classList.toggle('clickable', !!hit && (hit.type === 'car' || hit.type === 'place' || hit.type === 'fuel' || hit.type === 'highlight'));
  showTooltip(hit, event.clientX, event.clientY);
});

window.addEventListener('mouseup', event => {
  if (drag && !drag.moved && event.target === canvas && (event.ctrlKey || event.metaKey)) {
    const hit = hitTest(event.clientX, event.clientY);
    if (hit && hit.type === 'car') teleport({ car: hit.id });
    else teleport({ point: `${wx(event.clientX).toFixed(2)},${wz(event.clientY).toFixed(2)}` });
  } else if (drag && !drag.moved && event.target === canvas) {
    const hit = hitTest(event.clientX, event.clientY);
    if (hit && hit.type === 'highlight') {
      flyTo(highlight.x, highlight.z, Math.max(view.scale, 0.8));
    } else if (hit && hit.type === 'car') {
      select(hit.car[8] === 1 ? hit.id : hit.car[12] || selected);
    } else if (hit && hit.type === 'place') {
      selectIndustry(hit.item.i);
    } else if (hit && hit.type === 'fuel') {
      selectFuel(hit.item.i);
    } else if ((!hit || hit.type === 'track') && (selected || selectedIndustry || selectedFuel)) {
      clearSelection();
    }
  }
  drag = null;
  canvas.classList.remove('dragging');
  save();
});

function zoomAt(px, py, factor) {
  flight = null;
  const beforeX = wx(px);
  const beforeZ = wz(py);
  view.scale = clamp(view.scale * factor, MIN_SCALE, MAX_SCALE);
  if (!follow) {
    view.x = beforeX - (px - width / 2) / view.scale;
    view.z = beforeZ + (py - height / 2) / view.scale;
  }
  save();
}

canvas.addEventListener('wheel', event => {
  event.preventDefault();
  zoomAt(event.clientX, event.clientY, Math.exp(-event.deltaY * 0.0015));
}, { passive: false });

canvas.addEventListener('dblclick', event => zoomAt(event.clientX, event.clientY, 2));


const sidePanel = document.getElementById('panel');
const MODE_INDEX = { Manual: 0, Road: 1, Yard: 2, Waypoint: 3 };

function panelTarget() {
  if (selected) return { id: selected, path: 'train', header: 'X-Map-Plus-Train', name: locoName(selected) || 'Train' };
  if (selectedIndustry) {
    const place = places.industries.find(item => item.i === selectedIndustry);
    return { id: selectedIndustry, path: 'place', header: 'X-Map-Plus-Place', name: place ? place.n : 'Place' };
  }
  if (selectedFuel) {
    const fuel = places.fuel.find(item => item.i === selectedFuel);
    return { id: `fuel:${selectedFuel}`, path: 'place', header: 'X-Map-Plus-Place', name: fuel ? fuel.n : 'Fuel stop' };
  }
  if (listOpen) return { id: 'trains', list: true, name: 'Trains' };
  return null;
}

async function fetchPanel() {
  const target = panelTarget();
  if (!target || target.list) {
    panelInfo = null;
    renderPanel();
    return;
  }
  try {
    const response = await fetch(API + target.path, { cache: 'no-store', headers: { [target.header]: target.id } });
    const data = await response.json();
    const now = panelTarget();
    if (now && now.id === target.id) {
      panelInfo = Object.assign(data, { target: target.id });
      renderPanel();
    }
  } catch (error) {
  }
}

function refreshPanel() {
  renderPanel();
  fetchPanel();
}

async function panelLoop() {
  for (;;) {
    await fetchPanel();
    await sleep(1000);
  }
}

function plural(count, one, many) {
  return `${count} ${count === 1 ? one : many}`;
}

function kv(rows) {
  return `<div class="kv">${rows.filter(Boolean).map(([key, value]) => `<span>${escapeHtml(key)}</span><span>${value}</span>`).join('')}</div>`;
}

function meter(label, percent) {
  const color = percent <= 5 ? '#FF4038' : percent < 20 ? '#F0A340' : '#8CE6FF';
  return `<div class="meter-label"><span>${escapeHtml(label)}</span><span>${percent}%</span></div><div class="meter"><i style="width:${clamp(percent, 0, 100)}%;background:${color}"></i></div>`;
}

function badge(text, color) {
  return `<span class="badge" style="background:${color}">${escapeHtml(text)}</span>`;
}

function trainHtml(info) {
  const lead = info.engines.find(engine => engine.i === info.i) || info.engines[0];
  const mode = MODES[MODE_INDEX[info.ae.mode] ?? 0];
  const summary = info.summary;
  const parts = [];
  const subtitle = lead ? [lead.model, lead.kind].filter(Boolean).join(' · ') : '';
  parts.push(`<header>${backButton()}<div class="heading"><h2>${escapeHtml(info.n)}</h2><div class="sub">${escapeHtml(subtitle)}</div></div>${ordersSupport ? `<button class="${ordersOpen ? 'on' : ''}" data-orders-open title="${ordersApi ? 'Train Orders (O)' : UPDATE_ORDERS}">Orders</button>` : ''}<button class="follow${follow ? ' on' : ''}" data-follow title="Keep the map on this train (F)">${follow ? 'Following' : 'Follow'}</button><button data-teleport-car="${escapeHtml(selected || '')}" title="Move your camera to this train">Teleport</button><button class="close" data-close title="Close (Esc)">×</button></header>`);
  parts.push('<h3>Status</h3>');
  parts.push(kv([
    ['Speed', `${info.speed} mph${info.moving === 'Stopped' ? ', stopped' : `, ${info.moving.toLowerCase()}`}`],
    ['Engineer', `<span class="chip" style="background:${mode.color}">${escapeHtml(info.ae.on ? `AE ${info.ae.mode}` : 'Manual')}</span>${info.ae.on ? ` ${info.ae.forward ? 'forward' : 'reverse'}, up to ${info.ae.max} mph` : ''}`],
    info.ae.waypoint ? ['Heading to', escapeHtml(info.ae.waypoint)] : null,
    info.ae.status ? ['AE says', escapeHtml(info.ae.status)] : null
  ]));
  for (const alert of info.alerts) parts.push(`<div class="alert" style="color:${alert.c}">${escapeHtml(alert.t)}</div>`);
  parts.push('<h3>Train</h3>');
  parts.push(kv([
    ['Cars', `${summary.cars}${summary.cars ? ` (${summary.loaded} loaded, ${summary.empty} empty)` : ''}`],
    ['Locomotives', String(summary.engines)],
    ['Length', `${summary.length.toLocaleString()} ft`],
    ['Weight', `${summary.weight.toLocaleString()} tons`],
    ['Handbrakes', summary.handbrakes ? `<span class="warn">${summary.handbrakes} set</span>` : 'All released'],
    ['Air', summary.airGaps ? `<span class="warn">${plural(summary.airGaps, 'coupling', 'couplings')} without air</span>` : 'Connected'],
    summary.hotboxes ? ['Hotboxes', `<span class="danger">${summary.hotboxes}</span>`] : null,
    summary.destinations.length ? ['Destinations', summary.destinations.map(item => `${escapeHtml(item.n)} (${item.c})`).join('<br>')] : null
  ]));
  parts.push(`<h3>${info.engines.length === 1 ? 'Locomotive' : 'Locomotives'}</h3>`);
  for (const engine of info.engines) {
    parts.push('<div class="engine">');
    parts.push(`<div class="engine-name">${escapeHtml(engine.n)} <span class="sub">${escapeHtml([engine.model, engine.kind].filter(Boolean).join(' · '))}</span></div>`);
    for (const fuel of engine.fuel) parts.push(meter(fuel.n, fuel.p));
    parts.push(kv([
      ['Tractive effort', `${engine.te.toLocaleString()} lbf`],
      ['Weight', `${engine.weight} tons${engine.adhesive ? `, ${engine.adhesive} on drivers` : ''}`],
      engine.cylinders ? ['Cylinders', escapeHtml(engine.cylinders)] : null,
      engine.drivers ? ['Drivers', escapeHtml(engine.drivers)] : null,
      engine.boiler ? ['Boiler', escapeHtml(engine.boiler)] : null,
      engine.maxSpeed ? ['Top speed', `${engine.maxSpeed} mph`] : null,
      engine.throttle !== undefined ? ['Throttle', `${engine.throttle}%${engine.kind === 'Diesel' ? `, notch ${engine.notch}` : ''}`] : null,
      engine.reverser !== undefined ? ['Reverser', `${engine.reverser}%`] : null,
      engine.trainBrake !== undefined ? ['Train brake', `${engine.trainBrake}%`] : null,
      engine.engineBrake !== undefined ? ['Engine brake', `${engine.engineBrake}%`] : null,
      ['Condition', `${engine.condition}%`]
    ]));
    parts.push('</div>');
  }
  parts.push(`<h3>Consist (${info.consist.length})</h3>`);
  if (info.riders) {
    const riders = info.riders;
    const share = riders.cap > 0 ? ` (${Math.round(100 * riders.n / riders.cap)}%)` : '';
    const stops = riders.d.map(stop => `${escapeHtml(stop.n)} (${stop.c})`).join(', ');
    parts.push(`<div class="riders"><div><b>Passengers</b> ${riders.n}/${riders.cap}${share}</div>${stops ? `<div class="sub">To ${stops}</div>` : ''}</div>`);
  }
  info.consist.forEach((car, index) => {
    const swatch = car.k === 'engine' ? '#F6F1E6' : car.k === 'tender' ? '#B9B1A0' : car.color || '#FFFFFF';
    const badges = [
      car.derailed ? badge('DERAILED', '#FF4038') : '',
      car.hot ? badge('HOTBOX', '#FF4038') : '',
      car.hb ? badge('HANDBRAKE', '#BF6FF2') : '',
      car.oil !== null && car.oil < 50 ? badge(`OIL ${car.oil}%`, car.oil < 25 ? '#FF8C26' : '#C9A227') : ''
    ].join('');
    const details = [
      [car.model, `${car.len} ft`, `${car.w} tons`, car.cond < 100 ? `condition ${car.cond}%` : null].filter(Boolean).join(' · '),
      car.px ? car.load : car.load ? `Load: ${car.load}` : car.k === 'car' ? 'Empty' : null,
      car.dest || (car.k === 'car' && !car.px ? 'No waybill' : null)
    ].filter(Boolean);
    parts.push(`<div class="car" data-car="${escapeHtml(car.i)}"><span class="index">${index + 1}</span><i class="swatch" style="background:${swatch}"></i><div><div class="name">${escapeHtml(car.n)}${badges}</div>${details.map(line => `<div class="detail">${escapeHtml(line)}</div>`).join('')}</div></div>`);
  });
  if (info.orders) {
    const orders = info.orders;
    const state = orders.running ? '<span class="chip" style="background:#7BD66B">Running</span>' : '<span class="chip" style="background:#A2977F">Stopped</span>';
    const flags = [orders.repeat ? 'Repeat' : null, orders.smart ? 'Smart signals' : null].filter(Boolean).join(', ');
    parts.push('<h3>Train Orders</h3>');
    parts.push(`<div class="orders-state">${state}${orders.status ? ` ${escapeHtml(orders.status)}` : ''}${flags ? `<div class="sub">${escapeHtml(flags)}</div>` : ''}</div>`);
    orders.steps.forEach((step, index) => parts.push(`<div class="step ${step.s}">${index + 1}. ${escapeHtml(step.t)}</div>`));
  }
  return parts.join('');
}

function hoursText(hours) {
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} min`;
  if (hours < 48) return `${Math.round(hours)} h`;
  return `${Math.round(hours / 24)} days`;
}

function carRows(rows, title, color) {
  if (!rows.length) return '';
  const lines = [`<h3>${escapeHtml(title)} (${rows.length})</h3>`];
  rows.forEach((row, index) => {
    const details = [row.model, row.load, row.s].filter(Boolean);
    lines.push(`<div class="car" data-car="${escapeHtml(row.i)}"><span class="index">${index + 1}</span><i class="swatch" style="background:${color || row.color || '#FFFFFF'}"></i><div><div class="name">${escapeHtml(row.n)}</div>${details.map(line => `<div class="detail">${escapeHtml(line)}</div>`).join('')}</div></div>`);
  });
  return lines.join('');
}

function percentOf(value) {
  return Math.round(value <= 1.5 ? value * 100 : value);
}

function teleportPlaceButton(id, list) {
  const place = id ? list.find(item => item.i === id) : null;
  return place ? `<button data-teleport-point="${place.x.toFixed(2)},${place.z.toFixed(2)}" title="Move your camera to this place">Teleport</button>` : '';
}

function placePanelHtml(info) {
  const category = CATEGORIES[info.k] || CATEGORIES.freight;
  const parts = [];
  parts.push(`<header>${backButton()}<div class="heading"><h2 class="place">${escapeHtml(info.n)}</h2><div class="sub"><i class="dot" style="background:${category.color}"></i>${escapeHtml(category.label)}${info.t ? ` · ${escapeHtml(info.t)}` : ''}</div></div>${teleportPlaceButton(selectedIndustry, places.industries)}<button class="close" data-close title="Close (Esc)">×</button></header>`);
  for (const warning of info.w) parts.push(`<div class="alert warn">${richText(warning)}</div>`);
  const overview = [];
  if (info.ct !== null && info.ct !== undefined) overview.push(['Contract', `Tier ${info.ct}${info.nct !== null && info.nct !== undefined && info.nct !== info.ct ? `, tier ${info.nct} from tomorrow` : ''}`]);
  if (info.makes.length) overview.push(['Ships', info.makes.map(escapeHtml).join(', ')]);
  if (info.takes.length) overview.push(['Receives', info.takes.map(escapeHtml).join(', ')]);
  if (info.ic) {
    overview.push(['Last served', info.ic.last === null ? 'Not yet' : `${hoursText(info.ic.last)} ago`]);
    if (info.ic.extra !== null) overview.push(['Extra service', info.ic.extra > 0 ? `In ${hoursText(info.ic.extra)}` : 'Scheduled']);
  }
  if (overview.length) {
    parts.push('<h3>Overview</h3>');
    parts.push(kv(overview));
  }
  if (info.px) {
    parts.push('<h3>Passengers</h3>');
    parts.push(kv([
      ['Waiting', `<b>${info.px.total}</b>`],
      ...info.px.d.map(([name, count]) => [`To ${name}`, String(count)]),
      ['Last train', info.px.h === null ? 'Not yet' : `${hoursText(info.px.h)} ago`],
      ['Population', String(info.px.pop)],
      info.px.code ? ['Timetable code', escapeHtml(info.px.code)] : null,
      info.px.flag ? ['Stop', 'Flag stop'] : null
    ]));
  }
  if (info.sec.length) {
    parts.push('<h3>Storage and service</h3>');
    for (const section of info.sec) {
      parts.push(`<div class="sec-name">${escapeHtml(section.n)}</div>`);
      parts.push(kv(section.f.map(([label, text]) => [label || '·', richText(text)])));
    }
  }
  if (info.tracks.length) {
    parts.push('<h3>Tracks</h3>');
    for (const track of info.tracks) {
      const counts = [track.now ? `${track.now} on the track` : null, track.s ? `${track.s} spotted` : null, track.o ? `${track.o} on the way` : null].filter(Boolean).join(' · ');
      parts.push(`<div class="track-row"><span>${escapeHtml(track.n)}</span><span class="sub">${counts || 'Empty'}</span></div>`);
    }
  }
  parts.push(carRows(info.present, 'On these tracks now'));
  parts.push(carRows(info.coming, 'On the way', COMING_COLOR));
  if (info.perf.length) {
    parts.push('<h3>Recent performance</h3>');
    for (const [day, value] of info.perf) parts.push(meter(`Day ${day}`, percentOf(value)));
  }
  return parts.join('');
}

function fuelPanelHtml(info) {
  const category = CATEGORIES[info.k] || CATEGORIES.water;
  const parts = [];
  parts.push(`<header>${backButton()}<div class="heading"><h2 class="fuel">${escapeHtml(info.n)}</h2><div class="sub"><i class="dot" style="background:${category.color}"></i>${escapeHtml(category.label)}${info.t ? ` · ${escapeHtml(info.t)}` : ''}</div></div>${teleportPlaceButton(selectedFuel, places.fuel)}<button class="close" data-close title="Close (Esc)">×</button></header>`);
  parts.push('<h3>Stock</h3>');
  parts.push(kv([
    ['In stock', info.s === null ? 'Unlimited' : info.e ? '<span class="danger">Out of stock</span>' : escapeHtml(info.s)],
    info.src ? ['Supplied by', escapeHtml(info.src)] : null
  ]));
  if (info.supplies && info.supplies.length) {
    parts.push(`<h3>${escapeHtml(info.src || 'Supplier')} storage</h3>`);
    parts.push(kv(info.supplies.map(([name, quantity]) => [name, escapeHtml(quantity)])));
  }
  if (info.nearby.length) {
    parts.push('<h3>Also here</h3>');
    parts.push(kv(info.nearby.map(other => [other.n, other.s === null ? 'Unlimited' : other.e ? '<span class="danger">Out of stock</span>' : escapeHtml(other.s)])));
  }
  return parts.join('');
}

function renderPanel() {
  const target = panelTarget();
  if (!target) {
    sidePanel.classList.add('hidden');
    sidePanel.dataset.html = '';
    return;
  }
  const info = panelInfo && panelInfo.target === target.id ? panelInfo : null;
  let html;
  if (target.list) html = trainsListHtml();
  else if (info && info.ok) html = info.kind === 'industry' ? placePanelHtml(info) : info.kind === 'fuel' ? fuelPanelHtml(info) : trainHtml(info);
  else html = `<header>${backButton()}<div class="heading"><h2>${escapeHtml(target.name)}</h2><div class="sub">${escapeHtml(info && !info.ok ? info.reason : 'Loading…')}</div></div><button class="close" data-close title="Close (Esc)">×</button></header>`;
  if (sidePanel.dataset.html !== html) {
    sidePanel.innerHTML = html;
    sidePanel.dataset.html = html;
  }
  sidePanel.classList.remove('hidden');
}

sidePanel.addEventListener('click', event => {
  if (event.target.closest('[data-close]')) {
    if (panelTarget() && panelTarget().list) toggleList(false);
    else clearSelection();
    return;
  }
  if (event.target.closest('[data-back]')) {
    clearSelection();
    return;
  }
  if (event.target.closest('[data-orders-open]')) {
    toggleOrders();
    renderPanel();
    return;
  }
  const teleportCar = event.target.closest('[data-teleport-car]');
  if (teleportCar) {
    if (teleportCar.dataset.teleportCar) teleport({ car: teleportCar.dataset.teleportCar });
    return;
  }
  const teleportPoint = event.target.closest('[data-teleport-point]');
  if (teleportPoint) {
    teleport({ point: teleportPoint.dataset.teleportPoint });
    return;
  }
  if (event.target.closest('[data-follow]')) {
    setFollow(!follow && !!selected);
    return;
  }
  const trainRow = event.target.closest('[data-train]');
  if (trainRow) {
    pickTrain(trainRow.dataset.train);
    return;
  }
  const row = event.target.closest('[data-car]');
  const car = row ? curr.get(row.dataset.car) : null;
  if (!car) return;
  setFollow(false);
  view.x = (car[1] + car[3]) / 2;
  view.z = (car[2] + car[4]) / 2;
  view.scale = Math.max(view.scale, 1.2);
  save();
});

function selectIndustry(id) {
  selectedIndustry = id;
  selectedFuel = null;
  setFollow(false);
  select(null);
}

function selectFuel(id) {
  selectedFuel = id;
  selectedIndustry = null;
  setFollow(false);
  select(null);
}

function clearSelection() {
  setFollow(false);
  selectedIndustry = null;
  selectedFuel = null;
  select(null);
}

function drawIndustryTracks(g) {
  const industry = selectedPlace();
  if (!industry || !paths || !track || places.sv !== track.version || !industry.sg.length) return;
  const key = `${industry.i}|${places.sv}|${industry.sg.join(',')}`;
  if (industryPath.key !== key) {
    const path = new Path2D();
    for (const index of industry.sg) {
      const segment = segments[index];
      if (segment) curveTo(path, segment.p);
    }
    industryPath = { key, path };
  }
  worldTransform(g);
  g.lineCap = 'round';
  g.lineJoin = 'round';
  strokeWorld(g, industryPath.path, 'rgba(241, 184, 39, 0.45)', Math.max(trackWidth() + 9, 4.2 * view.scale + 6));
  g.setLineDash([]);
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function setSync(on) {
  sync = on;
  save();
  renderSync();
  showToast(sync ? 'This window follows the game\'s selection again' : 'This window keeps its own selection now');
  if (sync && gameSelected && gameSelected !== selected && locoById.has(gameSelected)) select(gameSelected, false);
}

function renderSync() {
  syncButton.innerHTML = `<svg viewBox="0 0 24 24">${sync ? LINKED : UNLINKED}</svg>`;
  syncButton.title = sync
    ? 'Synced with the game: selecting a train here selects it in the game, and the other way round. Click to give this window its own selection (G)'
    : 'This window keeps its own selection: the game doesn\'t change it. Click to sync with the game again (G)';
}

function setFollow(on) {
  follow = on;
  save();
  renderPanel();
}

function flyTo(x, z, scale, ms = 650) {
  flight = { x0: view.x, z0: view.z, s0: view.scale, x1: x, z1: z, s1: clamp(scale, MIN_SCALE, MAX_SCALE), start: performance.now(), ms };
}

function stepFlight() {
  if (!flight) return;
  const k = clamp((performance.now() - flight.start) / flight.ms, 0, 1);
  const e = k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2;
  view.x = flight.x0 + (flight.x1 - flight.x0) * e;
  view.z = flight.z0 + (flight.z1 - flight.z0) * e;
  view.scale = Math.exp(Math.log(flight.s0) + (Math.log(flight.s1) - Math.log(flight.s0)) * e);
  if (k >= 1) {
    flight = null;
    save();
  }
}

function fitView() {
  const target = fitTarget();
  if (!target) return;
  setFollow(false);
  flyTo(target.x, target.z, target.scale);
}

function townScale(town) {
  return clamp(Math.min(width, height) / (Math.max(town.r, 150) * 2.6), MIN_SCALE, MAX_SCALE);
}

function pickTrain(id) {
  const car = curr.get(id);
  select(id);
  setFollow(false);
  if (car) flyTo((car[1] + car[3]) / 2, (car[2] + car[4]) / 2, Math.max(view.scale, 0.8));
}

function pickCar(id) {
  const car = curr.get(id);
  if (!car) return;
  if (car[12] && locoById.has(car[12])) select(car[12]);
  else clearSelection();
  setFollow(false);
  ping = { id, until: performance.now() + 3000 };
  flyTo((car[1] + car[3]) / 2, (car[2] + car[4]) / 2, Math.max(view.scale, 1.5));
}

function pickTrack(place, town) {
  const industry = places.industries.find(item => item.t === town.n && item.tr.some(track => track.n === place.n));
  if (industry) selectIndustry(industry.i);
  else clearSelection();
  flyTo(place.x, place.z, Math.max(view.scale, 1.4));
}

function searchEntries() {
  const entries = [];
  for (const loco of locos) {
    if (!groups.has(loco.i)) continue;
    const mode = MODES[loco.m] || MODES[0];
    entries.push({ kind: 'Trains', label: loco.n, sub: `${loco.s} mph · ${loco.m ? 'AE ' : ''}${mode.name}`, color: mode.color, pick: () => pickTrain(loco.i) });
  }
  const seen = new Set();
  for (const industry of places.industries) {
    const category = CATEGORIES[industry.k] || CATEGORIES.freight;
    seen.add(`${industry.n.toLowerCase()}|${industry.t}`);
    entries.push({ kind: 'Places', label: industry.n, sub: `${category.label} · ${industry.t}`, color: category.color, pick: () => {
      selectIndustry(industry.i);
      flyTo(industry.x, industry.z, Math.max(view.scale, 0.9));
    } });
  }
  for (const town of (track && track.towns) || []) {
    entries.push({ kind: 'Towns', label: town.n, sub: `${town.p.length} ${town.p.length === 1 ? 'place' : 'places'}`, color: '#EDE7DA', pick: () => {
      clearSelection();
      flyTo(town.x, town.z, townScale(town));
    } });
    for (const place of town.p) {
      const key = `${place.n.toLowerCase()}|${town.n}`;
      if (seen.has(key)) continue;
      seen.add(key);
      entries.push({ kind: 'Places', label: place.n, sub: town.n, color: '#4DA6FF', pick: () => pickTrack(place, town) });
    }
  }
  for (const fuel of places.fuel) {
    const category = CATEGORIES[fuel.k] || CATEGORIES.water;
    entries.push({ kind: 'Places', label: fuel.n, sub: `${category.label} · ${fuel.t || ''}`, color: category.color, pick: () => {
      selectFuel(fuel.i);
      flyTo(fuel.x, fuel.z, Math.max(view.scale, 1.2));
    } });
  }
  for (const car of curr.values()) {
    if (car[8] === 1) continue;
    const inTrain = car[12] && locoById.has(car[12]);
    entries.push({ kind: 'Cars', label: `${car[6]} ${car[7]}`, sub: inTrain ? `In ${locoName(car[12])}'s train` : 'Standing', color: car[9] || '#FFFFFF', pick: () => pickCar(car[0]) });
  }
  return entries;
}

function runSearch() {
  const query = searchInput.value.trim().toLowerCase();
  if (!query) {
    search = { items: [], active: 0 };
    searchResults.classList.add('hidden');
    return;
  }
  const order = ['Trains', 'Towns', 'Places', 'Cars'];
  const scored = [];
  for (const entry of searchEntries()) {
    const label = entry.label.toLowerCase();
    let score = -1;
    if (label === query) score = 0;
    else if (label.startsWith(query)) score = 1;
    else if (label.split(/[\s\-'.]+/).some(word => word.startsWith(query))) score = 2;
    else if (label.includes(query)) score = 3;
    if (score >= 0) scored.push({ entry, score });
  }
  scored.sort((a, b) => a.score - b.score || a.entry.label.localeCompare(b.entry.label));
  const items = [];
  for (const kind of order) items.push(...scored.filter(item => item.entry.kind === kind).slice(0, 6).map(item => item.entry));
  search = { items, active: 0 };
  renderSearch();
}

function renderSearch() {
  if (!search.items.length) {
    searchResults.innerHTML = '<div class="empty">Nothing found</div>';
    searchResults.classList.remove('hidden');
    return;
  }
  let html = '';
  let group = '';
  search.items.forEach((item, index) => {
    if (item.kind !== group) {
      group = item.kind;
      html += `<div class="group">${escapeHtml(group)}</div>`;
    }
    html += `<div class="result${index === search.active ? ' active' : ''}" data-index="${index}"><i style="background:${item.color}"></i><span>${escapeHtml(item.label)}</span><span class="sub">${escapeHtml(item.sub)}</span></div>`;
  });
  searchResults.innerHTML = html;
  searchResults.classList.remove('hidden');
}

function choose(index) {
  const item = search.items[index];
  if (!item) return;
  searchInput.value = '';
  searchResults.classList.add('hidden');
  searchInput.blur();
  item.pick();
}

searchInput.addEventListener('input', runSearch);
searchInput.addEventListener('focus', () => {
  if (searchInput.value.trim()) runSearch();
});
searchInput.addEventListener('blur', () => searchResults.classList.add('hidden'));
searchInput.addEventListener('keydown', event => {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    if (!search.items.length) return;
    search.active = (search.active + (event.key === 'ArrowDown' ? 1 : search.items.length - 1)) % search.items.length;
    renderSearch();
  } else if (event.key === 'Enter') {
    event.preventDefault();
    choose(search.active);
  } else if (event.key === 'Escape') {
    searchInput.value = '';
    searchResults.classList.add('hidden');
    searchInput.blur();
  }
});
searchResults.addEventListener('mousedown', event => {
  event.preventDefault();
  const row = event.target.closest('[data-index]');
  if (row) choose(Number(row.dataset.index));
});

function trainsListHtml() {
  const trains = locos.filter(loco => groups.has(loco.i)).sort((a, b) => b.s - a.s || a.n.localeCompare(b.n));
  const parts = [`<header><div class="heading"><h2 class="list">Trains</h2><div class="sub">${trains.length} on the map</div></div><button class="close" data-close title="Close (Esc)">×</button></header>`];
  trains.forEach((loco, index) => {
    const mode = MODES[loco.m] || MODES[0];
    const consist = groups.get(loco.i) || [];
    const cars = consist.filter(car => car[8] === 0).length;
    const alert = consist.map(car => car[10]).find(Boolean);
    const details = [`${loco.m ? 'AE ' : ''}${mode.name}`, `${cars} ${cars === 1 ? 'car' : 'cars'}`, routeFor(loco.i) ? 'heading to a waypoint' : null].filter(Boolean).join(' · ');
    parts.push(`<div class="train-row" data-train="${escapeHtml(loco.i)}"><span class="index">${index + 1}</span><i class="swatch" style="background:${mode.color}"></i><div><div class="name">${escapeHtml(loco.n)} <span class="sub">${loco.s} mph</span>${alert ? `<span class="badge" style="background:${alert}">!</span>` : ''}</div><div class="detail">${escapeHtml(details)}</div></div></div>`);
  });
  return parts.join('');
}

function backButton() {
  return listOpen && (selected || selectedIndustry || selectedFuel) ? '<button class="back" data-back title="Back to the trains">‹</button>' : '';
}

function toggleList(open = !listOpen) {
  listOpen = open;
  if (open) {
    setFollow(false);
    selectedIndustry = null;
    selectedFuel = null;
    select(null);
  }
  document.querySelector('[data-action="trains"]').classList.toggle('on', listOpen);
  refreshPanel();
}

function buildLayers() {
  const hidden = new Set(layers.hiddenKinds || []);
  const toggle = (key, label, hint) => `<label class="switch-row" title="${escapeHtml(hint)}"><span>${escapeHtml(label)}</span><input type="checkbox" data-layer="${key}"${layers[key] ? ' checked' : ''}><i class="switch"></i></label>`;
  const kinds = FILTERS.map(filter => {
    const color = filter.color || (CATEGORIES[filter.key] || CATEGORIES.freight).color;
    return `<label><input type="checkbox" data-kind="${filter.key}"${hidden.has(filter.key) ? '' : ' checked'}><i style="background:${color}"></i>${escapeHtml(filter.label)}</label>`;
  }).join('');
  layersCard.innerHTML = '<h4>Map</h4>'
    + toggle('terrain', 'Terrain', "The terrain's relief, forests, fields and water")
    + toggle('classes', 'Track class', 'Color every track by its class')
    + toggle('condition', 'Track condition', "The game's yellow and rust stripes on degraded track")
    + toggle('industry', 'Industry tracks', 'Industry tracks in blue')
    + toggle('mainline', 'Mainline', 'The mainline in green')
    + toggle('names', 'Track names', "The names the game's map shows on passing tracks and other tracks")
    + toggle('limits', 'Speed limits', "The speed limit numbers the game's map shows along the track")
    + '<h4>Show</h4>'
    + toggle('places', 'Places', 'Industries, stations and fuel stops')
    + `<div class="kinds${layers.places ? '' : ' off'}">${kinds}<div class="kind-actions"><button data-all>Show all</button><button data-none>Hide all</button></div></div>`
    + toggle('route', 'Train route', "The selected train's route to its waypoint")
    + toggle('destinations', 'Train destinations', "The selected train's destination tracks: amber on the way, green spotted")
    + toggle('orders', 'Train orders', "The selected train's Train Orders as numbered markers")
    + toggle('signals', 'Signals', "Signals in their aspect's color")
    + toggle('fusees', 'Fusees', 'Fusees on the track, where the engineer stops')
    + toggle('cars', 'Cars', 'Every car; off leaves only the locomotives')
    + toggle('labels', 'Car numbers', 'Reporting marks on the cars')
    + toggle('alerts', 'Alerts', 'Car and train alerts');
}

function toggleLayers(open = layersCard.classList.contains('hidden')) {
  const button = document.querySelector('[data-action="layers"]');
  if (open) {
    buildLayers();
    const box = button.getBoundingClientRect();
    layersCard.style.top = `${box.bottom + 8}px`;
    layersCard.style.left = `${Math.max(12, Math.min(box.left, window.innerWidth - 290))}px`;
    layersCard.classList.remove('hidden');
  } else {
    layersCard.classList.add('hidden');
  }
  button.classList.toggle('on', open);
}

layersCard.addEventListener('change', event => {
  const target = event.target;
  if (target.dataset.layer) {
    layers[target.dataset.layer] = target.checked;
    if (target.dataset.layer === 'places') buildLayers();
  } else if (target.dataset.kind) {
    const hidden = new Set(layers.hiddenKinds || []);
    if (target.checked) hidden.delete(target.dataset.kind);
    else hidden.add(target.dataset.kind);
    layers.hiddenKinds = [...hidden];
  }
  save();
});

layersCard.addEventListener('click', event => {
  if (event.target.closest('[data-all]')) layers.hiddenKinds = [];
  else if (event.target.closest('[data-none]')) layers.hiddenKinds = FILTERS.map(filter => filter.key);
  else return;
  buildLayers();
  save();
});

window.addEventListener('mousedown', event => {
  if (!layersCard.classList.contains('hidden') && !layersCard.contains(event.target) && !event.target.closest('[data-action="layers"]')) toggleLayers(false);
});

document.getElementById('toolbar').addEventListener('click', event => {
  const button = event.target.closest('[data-action]');
  if (!button) return;
  const action = button.dataset.action;
  if (action === 'zoom-in') flyTo(view.x, view.z, view.scale * 2, 250);
  else if (action === 'zoom-out') flyTo(view.x, view.z, view.scale / 2, 250);
  else if (action === 'fit') fitView();
  else if (action === 'trains') toggleList();
  else if (action === 'sync') setSync(!sync);
  else if (action === 'orders') toggleOrders();
  else if (action === 'layers') toggleLayers();
  else if (action === 'fullscreen') {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen();
  }
});

function updateScale() {
  const key = `${view.scale}|${width}`;
  if (key === scaleKey) return;
  scaleKey = key;
  const feetPerPixel = 3.28084 / view.scale;
  const steps = [[10, 'ft'], [20, 'ft'], [50, 'ft'], [100, 'ft'], [200, 'ft'], [500, 'ft'], [1000, 'ft'], [2000, 'ft'], [0.5, 'mi'], [1, 'mi'], [2, 'mi'], [5, 'mi'], [10, 'mi'], [20, 'mi'], [50, 'mi']];
  let choice = steps[0];
  for (const step of steps) {
    const feet = step[1] === 'mi' ? step[0] * 5280 : step[0];
    if (feet / feetPerPixel <= 130) choice = step;
  }
  const feet = choice[1] === 'mi' ? choice[0] * 5280 : choice[0];
  scaleBar.style.width = `${Math.round(feet / feetPerPixel)}px`;
  scaleText.textContent = `${choice[0].toLocaleString()} ${choice[1]}`;
}

window.addEventListener('keydown', event => {
  if (event.target === searchInput || event.ctrlKey || event.metaKey || event.altKey) return;
  const key = event.key.toLowerCase();
  if (event.key === '/') {
    event.preventDefault();
    searchInput.focus();
    searchInput.select();
  } else if (event.key === 'Escape') {
    if (!menu.classList.contains('hidden')) hideMenu();
    else if (!layersCard.classList.contains('hidden')) toggleLayers(false);
    else if (ordersMenu) hideOrdersMenu();
    else if (ordersOpen) setOrdersOpen(false);
    else if (selected || selectedIndustry || selectedFuel) clearSelection();
    else if (listOpen) toggleList(false);
  } else if (key === 'f' && selected) {
    setFollow(!follow);
  } else if (key === 'l') {
    toggleLayers();
  } else if (key === 't') {
    toggleList();
  } else if (key === 'o' && ordersSupport) {
    toggleOrders();
  } else if (key === 'g') {
    setSync(!sync);
  }
});

window.addEventListener('resize', resize);
resize();
setFollow(follow);
renderSync();
requestAnimationFrame(frame);
loop('state', STATE_MS, handleState);
ordersLoop();
loop('routes', ROUTES_MS, handleRoutes);
loop('details', DETAILS_MS, handleDetails);
panelLoop();
loop('segments', SEGMENTS_MS, data => {
  if (data.ok) segmentInfo = data;
});
loop('places', PLACES_MS, data => {
  if (data.ok) places = data;
});
terrainLoop();
