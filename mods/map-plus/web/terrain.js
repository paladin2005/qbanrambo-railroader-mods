'use strict';

const MARGIN_TILES = 4;
const FEATHER = 260;
const RELIEF = 1.5;
const FAKE_DIM = 0.78;
const BUCKET = 50;
const CLEARANCE = 9;
const TRACK_REACH = 30;
const TREE_MPP = 1.6;
const TREE_CELL = 9.5;
const TREE_OCCUPANCY = 0.84;
const TREE_SHADOW = 0.45;
const RIVER_BUCKET = 32;
const RIVER_STEP = 1.2;
const RIVER_BANK = 3.5;
const RIVER_MIN_PIXELS = 0.6;
const SUN = (() => {
  const azimuth = 315 * Math.PI / 180;
  const altitude = 40 * Math.PI / 180;
  return { x: Math.sin(azimuth) * Math.cos(altitude), y: Math.cos(azimuth) * Math.cos(altitude), z: Math.sin(altitude) };
})();

const COLORS = {
  trees: [50, 82, 44],
  understory: [28, 44, 28],
  floor: [46, 58, 36],
  broadleaf: [66, 94, 46],
  conifer: [38, 62, 44],
  shrub: [92, 114, 62],
  grass: [116, 140, 76],
  crops: [[152, 146, 86], [128, 142, 72], [164, 150, 98]],
  wetland: [82, 110, 90],
  built: [136, 130, 118],
  bare: [150, 132, 100],
  gravel: [126, 122, 114],
  dirt: [140, 116, 84],
  ice: [222, 230, 234],
  rock: [122, 114, 100],
  mud: [112, 104, 80],
  mowed: [124, 146, 82],
  shallow: [70, 120, 142],
  deep: [34, 66, 98]
};

let world = null;
let rails = null;
let rivers = null;
let lastKey = -1;
let lastTile = null;

self.onmessage = event => {
  const message = event.data;
  try {
    if (message.type === 'load') {
      load(message.buffer);
      self.postMessage({ type: 'loaded', rect: world.rect });
    } else if (message.type === 'track') {
      setTrack(message.segments);
    } else if (message.type === 'rivers') {
      setRivers(message.rivers);
    } else if (message.type === 'render' && world) {
      const pixels = render(message);
      self.postMessage({ type: 'pixels', id: message.id, width: message.width, height: message.height, pixels: pixels.buffer }, [pixels.buffer]);
    }
  } catch (error) {
    self.postMessage({ type: 'error', id: message.id, message: String((error && error.message) || error) });
  }
};

function key(tx, ty) {
  return (tx + 4096) * 8192 + ty + 4096;
}

function tileAt(tx, ty) {
  const k = key(tx, ty);
  if (k !== lastKey) {
    lastKey = k;
    lastTile = world.tiles.get(k) || null;
  }
  return lastTile;
}

function load(buffer) {
  const reader = new DataView(buffer);
  if (String.fromCharCode(...new Uint8Array(buffer, 0, 4)) !== 'MPT1') throw new Error('Unknown terrain format');
  const dim = reader.getInt32(4, true);
  const cells = reader.getInt32(8, true);
  const count = reader.getInt32(12, true);
  const low = reader.getFloat32(16, true);
  const step = reader.getFloat32(20, true);
  const size = cells * cells;
  const tiles = new Map();
  let offset = 24;
  let minH = Infinity;
  let maxH = -Infinity;
  let minTx = Infinity, minTy = Infinity, maxTx = -Infinity, maxTy = -Infinity;
  for (let k = 0; k < count && offset + 8 + size * 3 <= buffer.byteLength; k++) {
    const tx = reader.getInt32(offset, true);
    const ty = reader.getInt32(offset + 4, true);
    offset += 8;
    const heights = new Uint16Array(buffer, offset, size);
    offset += size * 2;
    const biome = new Uint8Array(buffer, offset, size);
    offset += size;
    let sum = 0;
    let samples = 0;
    for (let i = 0; i < size; i += 9) {
      const h = low + heights[i] * step;
      sum += h;
      samples++;
      if (h < minH) minH = h;
      if (h > maxH) maxH = h;
    }
    tiles.set(key(tx, ty), { tx, ty, heights, biome, mean: sum / samples });
    minTx = Math.min(minTx, tx);
    minTy = Math.min(minTy, ty);
    maxTx = Math.max(maxTx, tx);
    maxTy = Math.max(maxTy, ty);
  }
  if (!tiles.size) throw new Error('No terrain tiles');
  for (const tile of tiles.values()) {
    const has = (dx, dy) => tiles.has(key(tile.tx + dx, tile.ty + dy));
    tile.west = !has(-1, 0);
    tile.east = !has(1, 0);
    tile.south = !has(0, -1);
    tile.north = !has(0, 1);
    tile.corners = [
      !tile.west && !tile.south && !has(-1, -1) ? [0, 0] : null,
      !tile.east && !tile.south && !has(1, -1) ? [dim, 0] : null,
      !tile.west && !tile.north && !has(-1, 1) ? [0, dim] : null,
      !tile.east && !tile.north && !has(1, 1) ? [dim, dim] : null
    ].filter(Boolean);
    tile.edged = tile.west || tile.east || tile.south || tile.north || tile.corners.length > 0;
  }
  const bx0 = minTx - MARGIN_TILES;
  const by0 = minTy - MARGIN_TILES;
  const bnx = maxTx - minTx + 1 + MARGIN_TILES * 2;
  const bny = maxTy - minTy + 1 + MARGIN_TILES * 2;
  const present = [...tiles.values()];
  const base = new Float32Array(bnx * bny);
  for (let j = 0; j < bny; j++) {
    for (let i = 0; i < bnx; i++) {
      const tile = tiles.get(key(bx0 + i, by0 + j));
      if (tile) {
        base[j * bnx + i] = tile.mean;
        continue;
      }
      let weighted = 0;
      let weights = 0;
      for (const other of present) {
        const dx = other.tx - (bx0 + i);
        const dy = other.ty - (by0 + j);
        const w = 1 / Math.pow(dx * dx + dy * dy, 1.5);
        weighted += other.mean * w;
        weights += w;
      }
      base[j * bnx + i] = weighted / weights;
    }
  }
  world = {
    dim, cells, cell: dim / cells, low, step, tiles, minH, maxH,
    amplitude: Math.min(420, Math.max(90, (maxH - minH) * 0.35)),
    base, bx0, by0, bnx, bny,
    rect: { x0: bx0 * dim, z0: by0 * dim, w: bnx * dim, h: bny * dim }
  };
  lastKey = -1;
}

function bucketKey(bx, bz) {
  return (bx + 32768) * 65536 + bz + 32768;
}

function setTrack(segments) {
  const buckets = new Map();
  for (const p of segments) {
    const chord = Math.hypot(p[6] - p[0], p[7] - p[1]);
    const net = Math.hypot(p[2] - p[0], p[3] - p[1]) + Math.hypot(p[4] - p[2], p[5] - p[3]) + Math.hypot(p[6] - p[4], p[7] - p[5]);
    const steps = Math.max(2, Math.ceil((chord + net) / 2 / 2.5));
    for (let k = 0; k <= steps; k++) {
      const t = k / steps;
      const u = 1 - t;
      const a = u * u * u;
      const b = 3 * u * u * t;
      const c = 3 * u * t * t;
      const d = t * t * t;
      const x = a * p[0] + b * p[2] + c * p[4] + d * p[6];
      const z = a * p[1] + b * p[3] + c * p[5] + d * p[7];
      const k2 = bucketKey(Math.floor(x / BUCKET), Math.floor(z / BUCKET));
      let list = buckets.get(k2);
      if (!list) {
        list = [];
        buckets.set(k2, list);
      }
      list.push(x, z);
    }
  }
  for (const [k, list] of buckets) buckets.set(k, Float32Array.from(list));
  rails = buckets;
}

function setRivers(paths) {
  const lists = new Map();
  let minBx = Infinity, minBz = Infinity, maxBx = -Infinity, maxBz = -Infinity;
  for (const path of paths || []) {
    const count = Math.floor(path.length / 3);
    if (count < 2) continue;
    const at = i => {
      const k = Math.max(0, Math.min(count - 1, i)) * 3;
      return [path[k], path[k + 1], path[k + 2]];
    };
    for (let i = 0; i < count - 1; i++) {
      const p0 = at(i - 1);
      const p1 = at(i);
      const p2 = at(i + 1);
      const p3 = at(i + 2);
      const length = Math.hypot(p2[0] - p1[0], p2[1] - p1[1]);
      const steps = Math.max(1, Math.ceil(length / RIVER_STEP));
      for (let s = 0; s < steps; s++) {
        const t = s / steps;
        const x = catmull(p0[0], p1[0], p2[0], p3[0], t);
        const z = catmull(p0[1], p1[1], p2[1], p3[1], t);
        const half = (p1[2] + (p2[2] - p1[2]) * t) / 2;
        const bx = Math.floor(x / RIVER_BUCKET);
        const bz = Math.floor(z / RIVER_BUCKET);
        const key = bucketKey(bx, bz);
        let list = lists.get(key);
        if (!list) {
          list = [];
          lists.set(key, list);
        }
        list.push(x, z, half);
        if (bx < minBx) minBx = bx;
        if (bz < minBz) minBz = bz;
        if (bx > maxBx) maxBx = bx;
        if (bz > maxBz) maxBz = bz;
      }
    }
  }
  if (!lists.size) {
    rivers = null;
    return;
  }
  const bx0 = minBx - 1;
  const bz0 = minBz - 1;
  const bw = maxBx - minBx + 3;
  const bh = maxBz - minBz + 3;
  const near = new Uint8Array(bw * bh);
  const buckets = new Map();
  for (const [key, list] of lists) {
    buckets.set(key, Float32Array.from(list));
    const bx = Math.floor(key / 65536) - 32768;
    const bz = (key % 65536) - 32768;
    for (let dz = -1; dz <= 1; dz++) {
      for (let dx = -1; dx <= 1; dx++) near[(bz + dz - bz0) * bw + bx + dx - bx0] = 1;
    }
  }
  rivers = { buckets, near, bx0, bz0, bw, bh };
}

function catmull(p0, p1, p2, p3, t) {
  return p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
}

const riverHit = { sdf: Infinity, half: 0 };

function riverAt(x, z, minHalf) {
  riverHit.sdf = Infinity;
  riverHit.half = 0;
  if (!rivers) return riverHit;
  const bx = Math.floor(x / RIVER_BUCKET);
  const bz = Math.floor(z / RIVER_BUCKET);
  const i = bx - rivers.bx0;
  const j = bz - rivers.bz0;
  if (i < 0 || j < 0 || i >= rivers.bw || j >= rivers.bh || !rivers.near[j * rivers.bw + i]) return riverHit;
  for (let dz = -1; dz <= 1; dz++) {
    for (let dx = -1; dx <= 1; dx++) {
      const list = rivers.buckets.get(bucketKey(bx + dx, bz + dz));
      if (!list) continue;
      for (let k = 0; k < list.length; k += 3) {
        const ex = list[k] - x;
        const ez = list[k + 1] - z;
        const half = Math.max(list[k + 2], minHalf);
        const sdf = Math.sqrt(ex * ex + ez * ez) - half;
        if (sdf < riverHit.sdf) {
          riverHit.sdf = sdf;
          riverHit.half = half;
        }
      }
    }
  }
  return riverHit;
}

function paintRiver(color, x, z, mpp, shade, fine) {
  const hit = riverAt(x, z, mpp * RIVER_MIN_PIXELS);
  const bank = Math.max(RIVER_BANK, mpp * 1.5);
  if (hit.sdf >= bank) return false;
  if (hit.sdf > 0) {
    blend(color, COLORS.mud, 0.35 * (1 - hit.sdf / bank));
    if (hit.sdf >= mpp * 0.5) return false;
  }
  const depth = Math.max(0, Math.min(1, -hit.sdf / Math.max(1.5, hit.half * 0.75)));
  const water = [0, 0, 0];
  water[0] = COLORS.shallow[0] + (COLORS.deep[0] - COLORS.shallow[0]) * depth * 0.9;
  water[1] = COLORS.shallow[1] + (COLORS.deep[1] - COLORS.shallow[1]) * depth * 0.9;
  water[2] = COLORS.shallow[2] + (COLORS.deep[2] - COLORS.shallow[2]) * depth * 0.9;
  const ripple = fine ? 0.97 + 0.06 * noise(x / 6 + z / 40, z / 6, 71) : 1;
  const glint = 0.94 + 0.08 * Math.min(1.3, shade);
  const alpha = Math.max(0, Math.min(1, 0.5 - hit.sdf / mpp));
  blend(color, [water[0] * ripple * glint, water[1] * ripple * glint, water[2] * ripple * glint], alpha);
  return true;
}

function trackDistance(x, z) {
  if (!rails) return TRACK_REACH;
  const bx = Math.floor(x / BUCKET);
  const bz = Math.floor(z / BUCKET);
  let best = TRACK_REACH * TRACK_REACH;
  for (let dz = -1; dz <= 1; dz++) {
    for (let dx = -1; dx <= 1; dx++) {
      const list = rails.get(bucketKey(bx + dx, bz + dz));
      if (!list) continue;
      for (let k = 0; k < list.length; k += 2) {
        const ex = list[k] - x;
        const ez = list[k + 1] - z;
        const d = ex * ex + ez * ez;
        if (d < best) best = d;
      }
    }
  }
  return Math.sqrt(best);
}

function clearanceAt(x, z) {
  return CLEARANCE + 3 * (noise(x / 30, z / 30, 97) - 0.5);
}

function hash(ix, iy, seed) {
  let h = (Math.imul(ix, 374761393) + Math.imul(iy, 668265263) + Math.imul(seed, 982451653)) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967296;
}

function noise(x, y, seed) {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const u = fx * fx * (3 - 2 * fx);
  const v = fy * fy * (3 - 2 * fy);
  const a = hash(ix, iy, seed);
  const b = hash(ix + 1, iy, seed);
  const c = hash(ix, iy + 1, seed);
  const d = hash(ix + 1, iy + 1, seed);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}

function perlin(x, y, seed) {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const u = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
  const v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
  const g = (gx, gy, dx, dy) => {
    const angle = hash(gx, gy, seed) * Math.PI * 2;
    return Math.cos(angle) * dx + Math.sin(angle) * dy;
  };
  const a = g(ix, iy, fx, fy);
  const b = g(ix + 1, iy, fx - 1, fy);
  const c = g(ix, iy + 1, fx, fy - 1);
  const d = g(ix + 1, iy + 1, fx - 1, fy - 1);
  return (a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v) * 1.4;
}

function ridged(x, z) {
  const wx = x + 1100 * perlin(x / 5200, z / 5200, 201);
  const wz = z + 1100 * perlin(x / 5200 + 7.3, z / 5200 + 2.9, 202);
  let sum = 0;
  let amplitude = 0.55;
  let frequency = 1 / 3400;
  let weight = 1;
  let norm = 0;
  for (let octave = 0; octave < 6; octave++) {
    let n = 1 - Math.abs(perlin(wx * frequency, wz * frequency, 11 + octave));
    n *= n * weight;
    weight = Math.max(0, Math.min(1, n * 1.7));
    sum += n * amplitude;
    norm += amplitude;
    amplitude *= 0.5;
    frequency *= 2.1;
  }
  return sum / norm;
}

function smoothstep(edge0, edge1, value) {
  const t = Math.max(0, Math.min(1, (value - edge0) / (edge1 - edge0)));
  return t * t * (3 - 2 * t);
}

function fade(distance) {
  return distance >= FEATHER ? 1 : smoothstep(0, FEATHER, distance);
}

function baseAt(x, z) {
  const u = Math.max(0, Math.min(world.bnx - 1.001, x / world.dim - world.bx0 - 0.5));
  const v = Math.max(0, Math.min(world.bny - 1.001, z / world.dim - world.by0 - 0.5));
  const i = Math.floor(u);
  const j = Math.floor(v);
  const fu = u - i;
  const fv = v - j;
  const n = world.bnx;
  const a = world.base[j * n + i];
  const b = world.base[j * n + i + 1];
  const c = world.base[(j + 1) * n + i];
  const d = world.base[(j + 1) * n + i + 1];
  return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
}

function realWeight(x, z) {
  const tile = tileAt(Math.floor(x / world.dim), Math.floor(z / world.dim));
  if (!tile) return 0;
  if (!tile.edged) return 1;
  const lx = x - tile.tx * world.dim;
  const lz = z - tile.ty * world.dim;
  let weight = 1;
  if (tile.west) weight *= fade(lx);
  if (tile.east) weight *= fade(world.dim - lx);
  if (tile.south) weight *= fade(lz);
  if (tile.north) weight *= fade(world.dim - lz);
  for (const [cx, cz] of tile.corners) weight *= fade(Math.hypot(lx - cx, lz - cz));
  return weight;
}

function cellHeight(gx, gy) {
  const tile = tileAt(Math.floor(gx / world.cells), Math.floor(gy / world.cells));
  if (!tile) return NaN;
  return world.low + tile.heights[(gy - tile.ty * world.cells) * world.cells + gx - tile.tx * world.cells] * world.step;
}

function realHeight(x, z) {
  const u = x / world.cell - 0.5;
  const v = z / world.cell - 0.5;
  const gx = Math.floor(u);
  const gy = Math.floor(v);
  const fu = u - gx;
  const fv = v - gy;
  const corners = [[gx, gy, (1 - fu) * (1 - fv)], [gx + 1, gy, fu * (1 - fv)], [gx, gy + 1, (1 - fu) * fv], [gx + 1, gy + 1, fu * fv]];
  let sum = 0;
  let weights = 0;
  for (const [cx, cy, w] of corners) {
    const h = cellHeight(cx, cy);
    if (Number.isNaN(h)) continue;
    sum += h * w;
    weights += w;
  }
  return weights > 0 ? sum / weights : NaN;
}

function realBiome(x, z) {
  const gx = Math.floor(x / world.cell);
  const gy = Math.floor(z / world.cell);
  const tile = tileAt(Math.floor(gx / world.cells), Math.floor(gy / world.cells));
  if (!tile) return 50;
  return tile.biome[(gy - tile.ty * world.cells) * world.cells + gx - tile.tx * world.cells];
}

function fakeBiome(x, z, height, base) {
  const relative = (height - base) / world.amplitude;
  if (relative < -0.42 + 0.18 * perlin(x / 900, z / 900, 91)) return perlin(x / 1200, z / 1200, 37) > 0.15 ? 70 : 30;
  if (perlin(x / 300, z / 300, 53) > 0.62) return 40;
  return 50;
}

function family(code) {
  if (code === 0) return 3;
  return Math.floor(code / 10);
}

function render(job) {
  const { x0, z0, mpp, width, height } = job;
  const grid = job.grid || Math.max(world.cell, mpp);
  const gx0 = Math.floor((x0 - 3 * grid) / grid);
  const gz0 = Math.floor((z0 - 3 * grid) / grid);
  const nx = Math.ceil((x0 + width * mpp + 3 * grid) / grid) - gx0 + 1;
  const nz = Math.ceil((z0 + height * mpp + 3 * grid) / grid) - gz0 + 1;
  const heights = new Float32Array(nx * nz);
  const biomes = new Uint8Array(nx * nz);
  const reals = new Float32Array(nx * nz);
  for (let j = 0; j < nz; j++) {
    for (let i = 0; i < nx; i++) {
      const x = (gx0 + i) * grid;
      const z = (gz0 + j) * grid;
      const index = j * nx + i;
      const weight = realWeight(x, z);
      let h = weight > 0 ? realHeight(x, z) : NaN;
      let code;
      if (weight >= 1 && !Number.isNaN(h)) {
        code = realBiome(x, z);
      } else {
        const base = baseAt(x, z);
        const fake = base + world.amplitude * (ridged(x, z) - 0.32) * 2;
        h = Number.isNaN(h) ? fake : h * weight + fake * (1 - weight);
        const jitter = (noise(x / 45, z / 45, 7) - 0.5) * 0.6;
        code = weight > 0.5 + jitter ? realBiome(x, z) : fakeBiome(x, z, fake, base);
      }
      heights[index] = h;
      biomes[index] = code;
      reals[index] = weight;
    }
  }
  const tracks = new Float32Array(nx * nz);
  for (let j = 0; j < nz; j++) {
    for (let i = 0; i < nx; i++) tracks[j * nx + i] = trackDistance((gx0 + i) * grid, (gz0 + j) * grid);
  }
  const distanceAt = (x, z) => {
    const u = Math.max(0, Math.min(nx - 1.001, x / grid - gx0));
    const v = Math.max(0, Math.min(nz - 1.001, z / grid - gz0));
    const i = Math.floor(u);
    const j = Math.floor(v);
    const fu = u - i;
    const fv = v - j;
    const a = tracks[j * nx + i];
    const b = tracks[j * nx + i + 1];
    const c = tracks[(j + 1) * nx + i];
    const d = tracks[(j + 1) * nx + i + 1];
    return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
  };
  const water = new Float32Array(nx * nz);
  const reach = Math.max(1, Math.round(10 / grid));
  for (let j = 0; j < nz; j++) {
    for (let i = 0; i < nx; i++) {
      let wet = 0;
      let total = 0;
      for (let dj = -reach; dj <= reach; dj++) {
        const jj = Math.min(nz - 1, Math.max(0, j + dj));
        for (let di = -reach; di <= reach; di++) {
          const ii = Math.min(nx - 1, Math.max(0, i + di));
          if (family(biomes[jj * nx + ii]) === 1) wet++;
          total++;
        }
      }
      water[j * nx + i] = wet / total;
    }
  }
  const hollow = new Float32Array(nx * nz);
  const span = Math.max(1, Math.round(Math.max(20, grid * 2) / grid));
  for (let j = 0; j < nz; j++) {
    for (let i = 0; i < nx; i++) {
      const left = heights[j * nx + Math.max(0, i - span)];
      const right = heights[j * nx + Math.min(nx - 1, i + span)];
      const down = heights[Math.max(0, j - span) * nx + i];
      const up = heights[Math.min(nz - 1, j + span) * nx + i];
      hollow[j * nx + i] = (left + right + down + up) / 4 - heights[j * nx + i];
    }
  }

  const surface = new Float32Array((width + 2) * (height + 2));
  for (let py = -1; py <= height; py++) {
    for (let px = -1; px <= width; px++) {
      const u = (x0 + (px + 0.5) * mpp) / grid - gx0;
      const v = (z0 + (py + 0.5) * mpp) / grid - gz0;
      surface[(py + 1) * (width + 2) + px + 1] = bicubic(heights, nx, u, v);
    }
  }

  const pixels = new Uint8ClampedArray(width * height * 4);
  const spreadU = new Float32Array(4);
  const spreadV = new Float32Array(4);
  const range = Math.max(1, world.maxH - world.minH);
  const fine = mpp <= 2.6;
  const medium = mpp <= 6;
  const trees = mpp <= TREE_MPP;
  const color = [0, 0, 0];
  const other = [0, 0, 0];
  for (let py = 0; py < height; py++) {
    for (let px = 0; px < width; px++) {
      const x = x0 + (px + 0.5) * mpp;
      const z = z0 + (py + 0.5) * mpp;
      const row = (py + 1) * (width + 2) + px + 1;
      const h = surface[row];
      const dx = (surface[row + 1] - surface[row - 1]) / (2 * mpp);
      const dz = (surface[row + width + 2] - surface[row - width - 2]) / (2 * mpp);
      const sx = -dx * RELIEF;
      const sz = -dz * RELIEF;
      const length = Math.sqrt(sx * sx + sz * sz + 1);
      const lambert = (sx * SUN.x + sz * SUN.y + SUN.z) / length;
      const shade = Math.max(0, Math.min(1.6, lambert / SUN.z));
      const slope = Math.sqrt(dx * dx + dz * dz);

      const u = x / grid - gx0;
      const v = z / grid - gz0;
      const i = Math.floor(u);
      const j = Math.floor(v);
      const tu = u - i;
      const tv = v - j;
      const n00 = j * nx + i;
      const n10 = n00 + 1;
      const n01 = n00 + nx;
      const n11 = n01 + 1;
      const w00 = (1 - tu) * (1 - tv);
      const w10 = tu * (1 - tv);
      const w01 = (1 - tu) * tv;
      const w11 = tu * tv;
      const wet = water[n00] * w00 + water[n10] * w10 + water[n01] * w01 + water[n11] * w11;
      const hollowness = hollow[n00] * w00 + hollow[n10] * w10 + hollow[n01] * w01 + hollow[n11] * w11;
      const real = reals[n00] * w00 + reals[n10] * w10 + reals[n01] * w01 + reals[n11] * w11;
      const near = tracks[n00] * w00 + tracks[n10] * w10 + tracks[n01] * w01 + tracks[n11] * w11;

      bsplineWeights(tu, spreadU);
      bsplineWeights(tv, spreadV);
      const firstFamily = family(biomes[n00]);
      let uniform = true;
      for (let r = -1; r <= 2 && uniform; r++) {
        for (let c = -1; c <= 2; c++) {
          if (family(biomes[(j + r) * nx + i + c]) !== firstFamily) {
            uniform = false;
            break;
          }
        }
      }
      let mix = 1;
      let firstCode = biomes[n00];
      let secondCode = firstCode;
      if (!uniform) {
        const scores = new Map();
        for (let r = -1; r <= 2; r++) {
          for (let c = -1; c <= 2; c++) {
            const code = biomes[(j + r) * nx + i + c];
            const f = family(code);
            const weight = spreadU[c + 1] * spreadV[r + 1];
            const entry = scores.get(f);
            if (entry) entry.w += weight;
            else scores.set(f, { code, w: weight });
          }
        }
        let best = null;
        let next = null;
        for (const [f, entry] of scores) {
          const score = entry.w + 0.16 * perlin(x / 14 + f * 13.1, z / 14, 5) + 0.05 * perlin(x / 3.5, z / 3.5, 6 + f);
          if (!best || score > best.score) {
            next = best;
            best = { code: entry.code, score };
          } else if (!next || score > next.score) {
            next = { code: entry.code, score };
          }
        }
        firstCode = best.code;
        secondCode = next ? next.code : best.code;
        const margin = next ? best.score - next.score : 1;
        mix = 0.5 + 0.5 * smoothstep(0, Math.max(0.015, (mpp / grid) * 0.8), margin);
      }

      const scene = { x, z, h, shade, slope, hollowness, wet, fine, medium, trees, range, near, mpp };
      paint(color, firstCode, scene);
      if (mix < 0.999) {
        paint(other, secondCode, scene);
        color[0] = color[0] * mix + other[0] * (1 - mix);
        color[1] = color[1] * mix + other[1] * (1 - mix);
        color[2] = color[2] * mix + other[2] * (1 - mix);
      }
      if (rivers) paintRiver(color, x, z, mpp, shade, fine);
      if (real < 0.999) {
        const keep = FAKE_DIM + (1 - FAKE_DIM) * real;
        const gray = (color[0] + color[1] + color[2]) / 3;
        const saturation = 0.75 + 0.25 * real;
        color[0] = (gray + (color[0] - gray) * saturation) * keep;
        color[1] = (gray + (color[1] - gray) * saturation) * keep;
        color[2] = (gray + (color[2] - gray) * saturation) * keep;
      }
      const out = (py * width + px) * 4;
      pixels[out] = color[0];
      pixels[out + 1] = color[1];
      pixels[out + 2] = color[2];
      pixels[out + 3] = 255;
    }
  }
  if (trees) plantTrees({ pixels, width, height, x0, z0, mpp, grid, gx0, gz0, nx, nz, biomes, reals, surface, distanceAt });
  return pixels;
}

function surfaceShade(surface, width, height, mpp, px, py) {
  const stride = width + 2;
  const cx = Math.max(0, Math.min(width - 1, Math.round(px)));
  const cy = Math.max(0, Math.min(height - 1, Math.round(py)));
  const row = (cy + 1) * stride + cx + 1;
  const dx = (surface[row + 1] - surface[row - 1]) / (2 * mpp);
  const dz = (surface[row + stride] - surface[row - stride]) / (2 * mpp);
  const sx = -dx * RELIEF;
  const sz = -dz * RELIEF;
  const lambert = (sx * SUN.x + sz * SUN.y + SUN.z) / Math.sqrt(sx * sx + sz * sz + 1);
  return Math.max(0, Math.min(1.6, lambert / SUN.z));
}

function plantTrees(job) {
  const { pixels, width, height, x0, z0, mpp, grid, gx0, gz0, nx, nz, biomes, reals, surface, distanceAt } = job;
  const reach = TREE_CELL * 1.4;
  const ix0 = Math.floor((x0 - reach) / TREE_CELL);
  const ix1 = Math.floor((x0 + width * mpp + reach) / TREE_CELL);
  const iz0 = Math.floor((z0 - reach) / TREE_CELL);
  const iz1 = Math.floor((z0 + height * mpp + reach) / TREE_CELL);
  const trees = [];
  for (let iz = iz0; iz <= iz1; iz++) {
    for (let ix = ix0; ix <= ix1; ix++) {
      if (hash(ix, iz, 30) > TREE_OCCUPANCY) continue;
      const tx = (ix + 0.12 + 0.76 * hash(ix, iz, 31)) * TREE_CELL;
      const tz = (iz + 0.12 + 0.76 * hash(ix, iz, 32)) * TREE_CELL;
      const u = Math.round(tx / grid - gx0);
      const v = Math.round(tz / grid - gz0);
      if (u < 0 || v < 0 || u >= nx || v >= nz || family(biomes[v * nx + u]) !== 5) continue;
      const conifer = noise(tx / 140, tz / 140, 33) + 0.4 * (hash(ix, iz, 34) - 0.5) > 0.56;
      const radius = conifer ? 2.6 + 1.5 * hash(ix, iz, 35) : 3.3 + 2.5 * hash(ix, iz, 35);
      if (distanceAt(tx, tz) < clearanceAt(tx, tz) + radius * 0.4) continue;
      if (rivers && riverAt(tx, tz, 0).sdf < radius * 0.5) continue;
      trees.push({ tx, tz, radius, conifer, top: hash(ix, iz, 36), tint: hash(ix, iz, 37), phase: hash(ix, iz, 38) * Math.PI * 2, seed: 40 + Math.floor(hash(ix, iz, 39) * 50), real: reals[v * nx + u] });
    }
  }
  if (!trees.length) return;
  const shadow = new Float32Array(width * height);
  for (const tree of trees) {
    const sx = tree.tx - SUN.x * tree.radius * 1.35;
    const sz = tree.tz - SUN.y * tree.radius * 1.35;
    stamp(sx, sz, tree.radius * 1.02, (index, distance, radius) => {
      const cover = Math.max(0, Math.min(1, (radius - distance) / (radius * 0.35)));
      if (cover > shadow[index]) shadow[index] = cover;
    });
  }
  for (let i = 0; i < shadow.length; i++) {
    if (!shadow[i]) continue;
    const keep = 1 - TREE_SHADOW * shadow[i];
    const o = i * 4;
    pixels[o] *= keep;
    pixels[o + 1] *= keep;
    pixels[o + 2] *= keep;
  }
  trees.sort((a, b) => a.top - b.top);
  const fineLeaves = mpp < 1;
  for (const tree of trees) {
    const base = tree.conifer ? COLORS.conifer : COLORS.broadleaf;
    const tint = 0.84 + 0.3 * tree.tint;
    const ground = 0.62 + 0.38 * Math.min(1.2, surfaceShade(surface, width, height, mpp, (tree.tx - x0) / mpp - 0.5, (tree.tz - z0) / mpp - 0.5));
    const keep = FAKE_DIM + (1 - FAKE_DIM) * tree.real;
    const saturation = 0.75 + 0.25 * tree.real;
    const r0 = base[0] * tint * ground;
    const g0 = base[1] * tint * ground;
    const b0 = base[2] * (0.9 + 0.2 * (1 - tree.tint)) * ground;
    const lobes = tree.conifer ? 7 : 5;
    const clump = Math.max(0.9, tree.radius / (tree.conifer ? 3.2 : 2.4));
    stamp(tree.tx, tree.tz, tree.radius * 1.12, (index, distance, outer, x, z) => {
      const angle = Math.atan2(z - tree.tz, x - tree.tx);
      const radius = tree.radius * (1 + (tree.conifer ? 0.1 : 0.08) * Math.sin(lobes * angle + tree.phase) + 0.05 * Math.sin((lobes + 3) * angle - tree.phase));
      if (distance > radius + mpp * 0.5) return;
      const ox = (x - tree.tx) / radius;
      const oz = (z - tree.tz) / radius;
      const d = Math.min(1, distance / radius);
      let lit;
      if (tree.conifer) {
        const slope = 0.9;
        const len = Math.sqrt(slope * slope * d * d + 1);
        const nxv = d > 0 ? ox / d * slope * d / len : 0;
        const nzv = d > 0 ? oz / d * slope * d / len : 0;
        lit = nxv * SUN.x + nzv * SUN.y + SUN.z / len;
      } else {
        const up = Math.sqrt(Math.max(0, 1 - d * d));
        lit = ox * SUN.x + oz * SUN.y + up * SUN.z;
      }
      let bright = (0.68 + 0.5 * Math.max(0, lit)) * (0.8 + 0.2 * Math.sqrt(Math.max(0, 1 - d * d)));
      bright *= 0.84 + 0.3 * noise(x / clump, z / clump, tree.seed);
      if (fineLeaves) bright *= 0.92 + 0.16 * noise(x / 0.9, z / 0.9, tree.seed + 1);
      let r = r0 * bright;
      let g = g0 * bright;
      let b = b0 * bright;
      if (tree.real < 0.999) {
        const gray = (r + g + b) / 3;
        r = (gray + (r - gray) * saturation) * keep;
        g = (gray + (g - gray) * saturation) * keep;
        b = (gray + (b - gray) * saturation) * keep;
      }
      const alpha = Math.max(0, Math.min(1, (radius - distance) / mpp + 0.5));
      const o = index * 4;
      pixels[o] += (r - pixels[o]) * alpha;
      pixels[o + 1] += (g - pixels[o + 1]) * alpha;
      pixels[o + 2] += (b - pixels[o + 2]) * alpha;
    });
  }

  function stamp(cx, cz, radius, visit) {
    const px0 = Math.max(0, Math.floor((cx - radius - x0) / mpp - 0.5));
    const px1 = Math.min(width - 1, Math.ceil((cx + radius - x0) / mpp - 0.5));
    const py0 = Math.max(0, Math.floor((cz - radius - z0) / mpp - 0.5));
    const py1 = Math.min(height - 1, Math.ceil((cz + radius - z0) / mpp - 0.5));
    const edge = radius + mpp * 0.5;
    for (let py = py0; py <= py1; py++) {
      const z = z0 + (py + 0.5) * mpp;
      const dz = z - cz;
      for (let px = px0; px <= px1; px++) {
        const x = x0 + (px + 0.5) * mpp;
        const dx = x - cx;
        const distance = Math.sqrt(dx * dx + dz * dz);
        if (distance > edge) continue;
        visit(py * width + px, distance, radius, x, z);
      }
    }
  }
}

function bsplineWeights(t, out) {
  const t2 = t * t;
  const t3 = t2 * t;
  out[0] = (1 - t) * (1 - t) * (1 - t) / 6;
  out[1] = (3 * t3 - 6 * t2 + 4) / 6;
  out[2] = (-3 * t3 + 3 * t2 + 3 * t + 1) / 6;
  out[3] = t3 / 6;
}

function cubic(p0, p1, p2, p3, t) {
  return p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
}

function bicubic(values, nx, u, v) {
  const i = Math.floor(u);
  const j = Math.floor(v);
  const tu = u - i;
  const tv = v - j;
  const rows = [];
  for (let r = -1; r <= 2; r++) {
    const base = (j + r) * nx + i;
    rows.push(cubic(values[base - 1], values[base], values[base + 1], values[base + 2], tu));
  }
  return cubic(rows[0], rows[1], rows[2], rows[3], tv);
}

function set(out, rgb, factor) {
  out[0] = rgb[0] * factor;
  out[1] = rgb[1] * factor;
  out[2] = rgb[2] * factor;
}

function blend(out, rgb, amount) {
  out[0] += (rgb[0] - out[0]) * amount;
  out[1] += (rgb[1] - out[1]) * amount;
  out[2] += (rgb[2] - out[2]) * amount;
}

function paint(out, code, scene) {
  const { x, z, h, shade, slope, hollowness, wet, fine, medium, trees, range, near, mpp } = scene;
  let f = family(code);
  const cleared = near < TRACK_REACH && (f === 5 || f === 4) && near < clearanceAt(x, z);
  if (cleared) f = 3;
  const light = 0.3 + 0.7 * shade;
  const occlusion = Math.max(0.7, Math.min(1.12, 1 - hollowness * 0.025));
  const elevation = 0.9 + 0.18 * ((h - world.minH) / range);
  if (f === 1) {
    const depth = smoothstep(0.35, 1, wet);
    out[0] = COLORS.shallow[0] + (COLORS.deep[0] - COLORS.shallow[0]) * depth;
    out[1] = COLORS.shallow[1] + (COLORS.deep[1] - COLORS.shallow[1]) * depth;
    out[2] = COLORS.shallow[2] + (COLORS.deep[2] - COLORS.shallow[2]) * depth;
    const ripple = fine ? 0.97 + 0.06 * noise(x / 6 + z / 40, z / 6, 71) : 1;
    const glint = 0.94 + 0.08 * Math.min(1.3, shade);
    out[0] *= ripple * glint;
    out[1] *= ripple * glint;
    out[2] *= ripple * glint;
    return;
  }
  let texture = 1;
  if (f === 5) {
    if (trees) {
      set(out, COLORS.floor, 1);
      texture = 0.86 + 0.24 * noise(x / 2.6, z / 2.6, 22) + 0.08 * (noise(x / 14, z / 14, 23) - 0.5);
    } else if (fine) {
      set(out, COLORS.trees, 1);
      const canopy = 0.6 * perlin(x / 10, z / 10, 22) + 0.4 * perlin(x / 5, z / 5, 24);
      blend(out, COLORS.understory, Math.max(0, -canopy) * 0.45);
      texture = 0.96 + 0.18 * canopy;
    } else {
      set(out, COLORS.trees, 1);
      texture = medium ? 0.84 + 0.2 * noise(x / 6, z / 6, 22) : 0.9 + 0.12 * noise(x / 40, z / 40, 23);
    }
    texture *= 1 - 0.08 * smoothstep(0.6, 1.2, slope);
  } else if (f === 4) {
    set(out, COLORS.grass, 1);
    if (fine) {
      const patch = perlin(x / 4, z / 4, 41) + (mpp < 1 ? 0.4 * perlin(z / 1.6, -x / 1.6, 43) : 0);
      const bush = smoothstep(-0.3, 0.35, patch);
      blend(out, COLORS.shrub, bush);
      texture = 0.97 - 0.12 * bush;
    } else {
      blend(out, COLORS.shrub, 0.8);
      texture = 0.9 + 0.16 * noise(x / 8, z / 8, 42);
    }
  } else if (f === 7) {
    const cell = 140;
    const fx = Math.floor(x / cell + noise(x / 90, z / 90, 61) * 0.6);
    const fz = Math.floor(z / cell + noise(x / 90, z / 90, 62) * 0.6);
    const pick = hash(fx, fz, 63);
    set(out, COLORS.crops[Math.floor(pick * COLORS.crops.length)], 1);
    texture = 0.95 + 0.1 * noise(x / 30, z / 30, 64);
    if (fine) {
      const angle = hash(fx, fz, 65) * Math.PI;
      texture *= 1 + 0.06 * Math.sin((x * Math.cos(angle) + z * Math.sin(angle)) * Math.PI / 1.8);
    }
  } else if (f === 6) {
    set(out, COLORS.wetland, 1);
    texture = 0.9 + 0.18 * noise(x / 5, z / 5, 81);
  } else if (f === 8) {
    set(out, COLORS.built, 1);
    texture = 0.9 + 0.16 * (fine ? hash(Math.floor(x / 4), Math.floor(z / 4), 82) : noise(x / 12, z / 12, 83));
  } else if (f === 2) {
    set(out, code === 21 || code === 23 ? COLORS.gravel : code === 22 ? COLORS.dirt : COLORS.bare, 1);
    texture = 0.88 + 0.2 * (fine ? hash(Math.floor(x * 1.3), Math.floor(z * 1.3), 84) : noise(x / 10, z / 10, 85));
  } else if (f === 9) {
    set(out, COLORS.ice, 1);
  } else {
    set(out, cleared ? COLORS.mowed : COLORS.grass, 1);
    texture = 0.93 + 0.1 * noise(x / 25, z / 25, 3) + (fine ? 0.05 * (noise(x / 3, z / 3, 4) - 0.5) : 0);
  }
  if (f !== 5 && f !== 4) blend(out, COLORS.rock, 0.55 * smoothstep(0.55, 1.1, slope));
  if (wet > 0.04) blend(out, COLORS.mud, smoothstep(0.04, 0.4, wet) * (f === 5 ? 0.25 : 0.6));
  const total = texture * light * occlusion * elevation * 0.97;
  out[0] *= total;
  out[1] *= total;
  out[2] *= total;
}
