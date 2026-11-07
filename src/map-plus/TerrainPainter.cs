using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace QbanRambo.MapPlus;

internal sealed class TerrainTile
{
  public int Tx;
  public int Ty;
  public ushort[] Heights;
  public byte[] Biome;
  public float Mean;
  public bool West;
  public bool East;
  public bool South;
  public bool North;
  public bool Edged;
  public float[] Corners;
}

internal sealed class TerrainWorld
{
  private const int MarginTiles = 4;

  private TerrainTile[] _grid;
  private int _gridX0;
  private int _gridY0;
  private int _gridWidth;
  private int _gridHeight;

  public int Dim;
  public int Cells;
  public float Cell;
  public float Low;
  public float Step;
  public float MinH;
  public float MaxH;
  public float Amplitude;
  public float[] Base;
  public int Bx0;
  public int By0;
  public int Bnx;
  public int Bny;
  public float X0;
  public float Z0;
  public float Width;
  public float Height;
  public int TileCount;

  public TerrainTile TileAt(int tx, int ty)
  {
    var i = tx - _gridX0;
    var j = ty - _gridY0;
    if ((uint)i >= (uint)_gridWidth || (uint)j >= (uint)_gridHeight) return null;
    return _grid[j * _gridWidth + i];
  }

  public static TerrainWorld Load(byte[] compressed)
  {
    byte[] data;
    using (var input = new MemoryStream(compressed))
    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
    using (var output = new MemoryStream(compressed.Length * 3))
    {
      gzip.CopyTo(output);
      data = output.ToArray();
    }
    if (data.Length < 24 || Encoding.ASCII.GetString(data, 0, 4) != "MPT1") throw new InvalidDataException("unknown terrain format");
    var world = new TerrainWorld
    {
      Dim = BitConverter.ToInt32(data, 4),
      Cells = BitConverter.ToInt32(data, 8),
      Low = BitConverter.ToSingle(data, 16),
      Step = BitConverter.ToSingle(data, 20)
    };
    var count = BitConverter.ToInt32(data, 12);
    var size = world.Cells * world.Cells;
    world.Cell = (float)world.Dim / world.Cells;
    var tiles = new List<TerrainTile>();
    var offset = 24;
    var minH = float.MaxValue;
    var maxH = float.MinValue;
    for (var k = 0; k < count && offset + 8 + size * 3 <= data.Length; k++)
    {
      var tile = new TerrainTile
      {
        Tx = BitConverter.ToInt32(data, offset),
        Ty = BitConverter.ToInt32(data, offset + 4),
        Heights = new ushort[size],
        Biome = new byte[size]
      };
      offset += 8;
      Buffer.BlockCopy(data, offset, tile.Heights, 0, size * 2);
      offset += size * 2;
      Buffer.BlockCopy(data, offset, tile.Biome, 0, size);
      offset += size;
      double sum = 0;
      var samples = 0;
      for (var i = 0; i < size; i += 9)
      {
        var h = world.Low + tile.Heights[i] * world.Step;
        sum += h;
        samples++;
        if (h < minH) minH = h;
        if (h > maxH) maxH = h;
      }
      tile.Mean = (float)(sum / samples);
      tiles.Add(tile);
    }
    if (tiles.Count == 0) throw new InvalidDataException("no terrain tiles");
    int minTx = int.MaxValue, minTy = int.MaxValue, maxTx = int.MinValue, maxTy = int.MinValue;
    foreach (var tile in tiles)
    {
      minTx = Math.Min(minTx, tile.Tx);
      minTy = Math.Min(minTy, tile.Ty);
      maxTx = Math.Max(maxTx, tile.Tx);
      maxTy = Math.Max(maxTy, tile.Ty);
    }
    world._gridX0 = minTx;
    world._gridY0 = minTy;
    world._gridWidth = maxTx - minTx + 1;
    world._gridHeight = maxTy - minTy + 1;
    world._grid = new TerrainTile[world._gridWidth * world._gridHeight];
    foreach (var tile in tiles) world._grid[(tile.Ty - minTy) * world._gridWidth + tile.Tx - minTx] = tile;
    var dim = (float)world.Dim;
    foreach (var tile in tiles)
    {
      bool Has(int dx, int dy) => world.TileAt(tile.Tx + dx, tile.Ty + dy) != null;
      tile.West = !Has(-1, 0);
      tile.East = !Has(1, 0);
      tile.South = !Has(0, -1);
      tile.North = !Has(0, 1);
      var corners = new List<float>();
      if (!tile.West && !tile.South && !Has(-1, -1)) corners.AddRange(new[] { 0f, 0f });
      if (!tile.East && !tile.South && !Has(1, -1)) corners.AddRange(new[] { dim, 0f });
      if (!tile.West && !tile.North && !Has(-1, 1)) corners.AddRange(new[] { 0f, dim });
      if (!tile.East && !tile.North && !Has(1, 1)) corners.AddRange(new[] { dim, dim });
      tile.Corners = corners.ToArray();
      tile.Edged = tile.West || tile.East || tile.South || tile.North || tile.Corners.Length > 0;
    }
    world.Bx0 = minTx - MarginTiles;
    world.By0 = minTy - MarginTiles;
    world.Bnx = maxTx - minTx + 1 + MarginTiles * 2;
    world.Bny = maxTy - minTy + 1 + MarginTiles * 2;
    world.Base = new float[world.Bnx * world.Bny];
    for (var j = 0; j < world.Bny; j++)
    {
      for (var i = 0; i < world.Bnx; i++)
      {
        var own = world.TileAt(world.Bx0 + i, world.By0 + j);
        if (own != null)
        {
          world.Base[j * world.Bnx + i] = own.Mean;
          continue;
        }
        double weighted = 0;
        double weights = 0;
        foreach (var other in tiles)
        {
          double dx = other.Tx - (world.Bx0 + i);
          double dy = other.Ty - (world.By0 + j);
          var d2 = dx * dx + dy * dy;
          var w = 1.0 / (d2 * Math.Sqrt(d2));
          weighted += other.Mean * w;
          weights += w;
        }
        world.Base[j * world.Bnx + i] = (float)(weighted / weights);
      }
    }
    world.MinH = minH;
    world.MaxH = maxH;
    world.Amplitude = Math.Min(420f, Math.Max(90f, (maxH - minH) * 0.35f));
    world.X0 = world.Bx0 * dim;
    world.Z0 = world.By0 * dim;
    world.Width = world.Bnx * dim;
    world.Height = world.Bny * dim;
    world.TileCount = tiles.Count;
    return world;
  }
}

internal sealed class TerrainRails
{
  private const float Bucket = 50f;

  private readonly Dictionary<long, float[]> _buckets;

  private TerrainRails(Dictionary<long, float[]> buckets)
  {
    _buckets = buckets;
  }

  public static TerrainRails Build(List<float[]> segments)
  {
    var lists = new Dictionary<long, List<float>>();
    foreach (var p in segments)
    {
      var chord = Hypot(p[6] - p[0], p[7] - p[1]);
      var net = Hypot(p[2] - p[0], p[3] - p[1]) + Hypot(p[4] - p[2], p[5] - p[3]) + Hypot(p[6] - p[4], p[7] - p[5]);
      var steps = Math.Max(2, (int)Math.Ceiling((chord + net) / 2f / 2.5f));
      for (var k = 0; k <= steps; k++)
      {
        var t = (float)k / steps;
        var u = 1f - t;
        var a = u * u * u;
        var b = 3f * u * u * t;
        var c = 3f * u * t * t;
        var d = t * t * t;
        var x = a * p[0] + b * p[2] + c * p[4] + d * p[6];
        var z = a * p[1] + b * p[3] + c * p[5] + d * p[7];
        var key = Key(TerrainPainter.Floor(x / Bucket), TerrainPainter.Floor(z / Bucket));
        if (!lists.TryGetValue(key, out var list))
        {
          list = new List<float>();
          lists[key] = list;
        }
        list.Add(x);
        list.Add(z);
      }
    }
    var buckets = new Dictionary<long, float[]>(lists.Count);
    foreach (var entry in lists) buckets[entry.Key] = entry.Value.ToArray();
    return new TerrainRails(buckets);
  }

  public float Distance(float x, float z, float reach)
  {
    var bx = TerrainPainter.Floor(x / Bucket);
    var bz = TerrainPainter.Floor(z / Bucket);
    var best = reach * reach;
    for (var dz = -1; dz <= 1; dz++)
    {
      for (var dx = -1; dx <= 1; dx++)
      {
        if (!_buckets.TryGetValue(Key(bx + dx, bz + dz), out var list)) continue;
        for (var k = 0; k < list.Length; k += 2)
        {
          var ex = list[k] - x;
          var ez = list[k + 1] - z;
          var d = ex * ex + ez * ez;
          if (d < best) best = d;
        }
      }
    }
    return (float)Math.Sqrt(best);
  }

  private static long Key(int bx, int bz) => ((long)(bx + 1048576) << 21) | (long)(bz + 1048576);

  private static float Hypot(float x, float y) => (float)Math.Sqrt(x * x + y * y);
}

internal sealed class TerrainRivers
{
  private const float Bucket = 32f;
  private const float Step = 1.2f;

  private readonly Dictionary<long, float[]> _buckets;
  private readonly bool[] _near;
  private readonly int _bx0;
  private readonly int _bz0;
  private readonly int _width;
  private readonly int _height;

  private TerrainRivers(Dictionary<long, float[]> buckets, bool[] near, int bx0, int bz0, int width, int height)
  {
    _buckets = buckets;
    _near = near;
    _bx0 = bx0;
    _bz0 = bz0;
    _width = width;
    _height = height;
  }

  public static TerrainRivers Build(List<float[]> paths)
  {
    var lists = new Dictionary<long, List<float>>();
    int minBx = int.MaxValue, minBz = int.MaxValue, maxBx = int.MinValue, maxBz = int.MinValue;
    var cells = new List<(int Bx, int Bz)>();
    foreach (var path in paths ?? new List<float[]>())
    {
      var count = path.Length / 3;
      if (count < 2) continue;
      for (var i = 0; i < count - 1; i++)
      {
        var p0 = Math.Max(0, i - 1) * 3;
        var p1 = i * 3;
        var p2 = (i + 1) * 3;
        var p3 = Math.Min(count - 1, i + 2) * 3;
        var length = (float)Math.Sqrt((path[p2] - path[p1]) * (path[p2] - path[p1]) + (path[p2 + 1] - path[p1 + 1]) * (path[p2 + 1] - path[p1 + 1]));
        var steps = Math.Max(1, (int)Math.Ceiling(length / Step));
        for (var s = 0; s < steps; s++)
        {
          var t = (float)s / steps;
          var x = Catmull(path[p0], path[p1], path[p2], path[p3], t);
          var z = Catmull(path[p0 + 1], path[p1 + 1], path[p2 + 1], path[p3 + 1], t);
          var half = (path[p1 + 2] + (path[p2 + 2] - path[p1 + 2]) * t) / 2f;
          var bx = TerrainPainter.Floor(x / Bucket);
          var bz = TerrainPainter.Floor(z / Bucket);
          var key = Key(bx, bz);
          if (!lists.TryGetValue(key, out var list))
          {
            list = new List<float>();
            lists[key] = list;
            cells.Add((bx, bz));
          }
          list.Add(x);
          list.Add(z);
          list.Add(half);
          minBx = Math.Min(minBx, bx);
          minBz = Math.Min(minBz, bz);
          maxBx = Math.Max(maxBx, bx);
          maxBz = Math.Max(maxBz, bz);
        }
      }
    }
    if (lists.Count == 0) return null;
    var bx0 = minBx - 1;
    var bz0 = minBz - 1;
    var width = maxBx - minBx + 3;
    var height = maxBz - minBz + 3;
    var near = new bool[width * height];
    foreach (var (bx, bz) in cells)
    {
      for (var dz = -1; dz <= 1; dz++)
      {
        for (var dx = -1; dx <= 1; dx++) near[(bz + dz - bz0) * width + bx + dx - bx0] = true;
      }
    }
    var buckets = new Dictionary<long, float[]>(lists.Count);
    foreach (var entry in lists) buckets[entry.Key] = entry.Value.ToArray();
    return new TerrainRivers(buckets, near, bx0, bz0, width, height);
  }

  public bool Query(float x, float z, float minHalf, out float sdf, out float half)
  {
    sdf = float.MaxValue;
    half = 0f;
    var bx = TerrainPainter.Floor(x / Bucket);
    var bz = TerrainPainter.Floor(z / Bucket);
    var i = bx - _bx0;
    var j = bz - _bz0;
    if (i < 0 || j < 0 || i >= _width || j >= _height || !_near[j * _width + i]) return false;
    for (var dz = -1; dz <= 1; dz++)
    {
      for (var dx = -1; dx <= 1; dx++)
      {
        if (!_buckets.TryGetValue(Key(bx + dx, bz + dz), out var list)) continue;
        for (var k = 0; k < list.Length; k += 3)
        {
          var ex = list[k] - x;
          var ez = list[k + 1] - z;
          var width = Math.Max(list[k + 2], minHalf);
          var distance = (float)Math.Sqrt(ex * ex + ez * ez) - width;
          if (distance >= sdf) continue;
          sdf = distance;
          half = width;
        }
      }
    }
    return sdf < float.MaxValue;
  }

  private static float Catmull(float p0, float p1, float p2, float p3, float t)
  {
    return p1 + 0.5f * t * (p2 - p0 + t * (2f * p0 - 5f * p1 + 4f * p2 - p3 + t * (3f * (p1 - p2) + p3 - p0)));
  }

  private static long Key(int bx, int bz) => ((long)(bx + 1048576) << 21) | (long)(bz + 1048576);
}

internal sealed class TerrainPainter
{
  private const int Margin = 7;
  private const float Feather = 260f;
  private const float Relief = 1.5f;
  private const float FakeDim = 0.78f;
  private const float Clearance = 9f;
  private const float TrackReach = 30f;
  private const float EdgeFade = 1200f;
  private const float Brightness = 0.8f;
  private const float Saturation = 0.9f;
  private const float RiverBank = 3.5f;
  private const float RiverMinPixels = 0.6f;

  private static readonly float SunX;
  private static readonly float SunY;
  private static readonly float SunZ;
  private static readonly float[] GradX = new float[256];
  private static readonly float[] GradY = new float[256];

  private static readonly Rgb Trees = new(50, 82, 44);
  private static readonly Rgb Broadleaf = new(70, 100, 50);
  private static readonly Rgb Understory = new(28, 44, 28);
  private static readonly Rgb Shrub = new(92, 114, 62);
  private static readonly Rgb Grass = new(116, 140, 76);
  private static readonly Rgb[] Crops = { new(152, 146, 86), new(128, 142, 72), new(164, 150, 98) };
  private static readonly Rgb Wetland = new(82, 110, 90);
  private static readonly Rgb Built = new(136, 130, 118);
  private static readonly Rgb Bare = new(150, 132, 100);
  private static readonly Rgb Gravel = new(126, 122, 114);
  private static readonly Rgb Dirt = new(140, 116, 84);
  private static readonly Rgb Ice = new(222, 230, 234);
  private static readonly Rgb Rock = new(122, 114, 100);
  private static readonly Rgb Mud = new(112, 104, 80);
  private static readonly Rgb Mowed = new(124, 146, 82);
  private static readonly Rgb Shallow = new(70, 120, 142);
  private static readonly Rgb Middle = new(48, 90, 122);
  private static readonly Rgb Deep = new(34, 66, 98);

  private readonly int[] _familyIds = new int[16];
  private readonly int[] _familyCodes = new int[16];
  private readonly float[] _familyWeights = new float[16];
  private readonly float[] _spreadU = new float[4];
  private readonly float[] _spreadV = new float[4];

  private float[] _heights = new float[0];
  private byte[] _biomes = new byte[0];
  private byte[] _families = new byte[0];
  private float[] _reals = new float[0];
  private float[] _tracks = new float[0];
  private float[] _water = new float[0];
  private float[] _deep = new float[0];
  private float[] _rows = new float[0];
  private float[] _hollow = new float[0];
  private float[] _surface = new float[0];

  private bool _fine;
  private bool _medium;
  private float _mpp;
  private float _range;

  static TerrainPainter()
  {
    var azimuth = 315.0 * Math.PI / 180.0;
    var altitude = 40.0 * Math.PI / 180.0;
    SunX = (float)(Math.Sin(azimuth) * Math.Cos(altitude));
    SunY = (float)(Math.Cos(azimuth) * Math.Cos(altitude));
    SunZ = (float)Math.Sin(altitude);
    for (var i = 0; i < 256; i++)
    {
      var angle = (i + 0.5) / 256.0 * Math.PI * 2.0;
      GradX[i] = (float)Math.Cos(angle);
      GradY[i] = (float)Math.Sin(angle);
    }
  }

  public TerrainPainter(TerrainWorld world)
  {
    World = world;
  }

  public TerrainWorld World { get; }

  public void Render(float x0, float z0, float mpp, float grid, int width, int height, TerrainRails rails, TerrainRivers rivers, byte[] output, int offset)
  {
    var world = World;
    var gx0 = Floor((x0 - Margin * grid) / grid);
    var gz0 = Floor((z0 - Margin * grid) / grid);
    var nx = (int)Math.Ceiling((x0 + width * mpp + Margin * grid) / grid) - gx0 + 1;
    var nz = (int)Math.Ceiling((z0 + height * mpp + Margin * grid) / grid) - gz0 + 1;
    var cells = nx * nz;
    Ensure(ref _heights, cells);
    Ensure(ref _biomes, cells);
    Ensure(ref _families, cells);
    Ensure(ref _reals, cells);
    Ensure(ref _tracks, cells);
    Ensure(ref _water, cells);
    Ensure(ref _deep, cells);
    Ensure(ref _rows, cells);
    Ensure(ref _hollow, cells);
    var heights = _heights;
    var biomes = _biomes;
    var families = _families;
    var reals = _reals;
    for (var j = 0; j < nz; j++)
    {
      for (var i = 0; i < nx; i++)
      {
        var x = (gx0 + i) * grid;
        var z = (gz0 + j) * grid;
        var index = j * nx + i;
        var weight = RealWeight(x, z);
        var h = weight > 0f ? RealHeight(x, z) : float.NaN;
        int code;
        if (weight >= 1f && !float.IsNaN(h))
        {
          code = RealBiome(x, z);
        }
        else
        {
          var baseHeight = BaseAt(x, z);
          var fake = baseHeight + world.Amplitude * (Ridged(x, z) - 0.32f) * 2f;
          h = float.IsNaN(h) ? fake : h * weight + fake * (1f - weight);
          var jitter = (Noise(x / 45f, z / 45f, 7) - 0.5f) * 0.6f;
          code = weight > 0.5f + jitter ? RealBiome(x, z) : FakeBiome(x, z, fake, baseHeight);
        }
        heights[index] = h;
        biomes[index] = (byte)code;
        families[index] = (byte)Family(code);
        reals[index] = weight;
      }
    }

    var tracks = _tracks;
    var cleared = rails != null && mpp <= 6f;
    for (var j = 0; j < nz; j++)
    {
      for (var i = 0; i < nx; i++)
      {
        tracks[j * nx + i] = cleared ? rails.Distance((gx0 + i) * grid, (gz0 + j) * grid, TrackReach) : TrackReach;
      }
    }

    var water = _water;
    var deep = _deep;
    WaterShare(nx, nz, Math.Max(1, (int)Math.Round(10f / grid)), water);
    WaterShare(nx, nz, Math.Max(1, (int)Math.Round(30f / grid)), deep);

    var hollow = _hollow;
    var span = Math.Max(1, (int)Math.Round(Math.Max(20f, grid * 2f) / grid));
    for (var j = 0; j < nz; j++)
    {
      for (var i = 0; i < nx; i++)
      {
        var left = heights[j * nx + Math.Max(0, i - span)];
        var right = heights[j * nx + Math.Min(nx - 1, i + span)];
        var down = heights[Math.Max(0, j - span) * nx + i];
        var up = heights[Math.Min(nz - 1, j + span) * nx + i];
        hollow[j * nx + i] = (left + right + down + up) / 4f - heights[j * nx + i];
      }
    }

    var stride = width + 2;
    Ensure(ref _surface, stride * (height + 2));
    var surface = _surface;
    for (var py = -1; py <= height; py++)
    {
      for (var px = -1; px <= width; px++)
      {
        var u = (x0 + (px + 0.5f) * mpp) / grid - gx0;
        var v = (z0 + (py + 0.5f) * mpp) / grid - gz0;
        surface[(py + 1) * stride + px + 1] = Bicubic(heights, nx, u, v);
      }
    }

    _range = Math.Max(1f, world.MaxH - world.MinH);
    _fine = mpp <= 2.6f;
    _medium = mpp <= 6f;
    _mpp = mpp;
    var spreadU = _spreadU;
    var spreadV = _spreadV;
    var blendWidth = Math.Max(0.015f, mpp / grid * 0.8f);
    var right0 = world.X0 + world.Width;
    var top0 = world.Z0 + world.Height;
    for (var py = 0; py < height; py++)
    {
      var z = z0 + (py + 0.5f) * mpp;
      for (var px = 0; px < width; px++)
      {
        var x = x0 + (px + 0.5f) * mpp;
        var row = (py + 1) * stride + px + 1;
        var h = surface[row];
        var dx = (surface[row + 1] - surface[row - 1]) / (2f * mpp);
        var dz = (surface[row + stride] - surface[row - stride]) / (2f * mpp);
        var sx = -dx * Relief;
        var sz = -dz * Relief;
        var length = (float)Math.Sqrt(sx * sx + sz * sz + 1f);
        var lambert = (sx * SunX + sz * SunY + SunZ) / length;
        var shade = Clamp(lambert / SunZ, 0f, 1.6f);
        var slope = (float)Math.Sqrt(dx * dx + dz * dz);

        var gu = x / grid - gx0;
        var gv = z / grid - gz0;
        var i = Floor(gu);
        var j = Floor(gv);
        var tu = gu - i;
        var tv = gv - j;
        var n00 = j * nx + i;
        var n10 = n00 + 1;
        var n01 = n00 + nx;
        var n11 = n01 + 1;
        var w00 = (1f - tu) * (1f - tv);
        var w10 = tu * (1f - tv);
        var w01 = (1f - tu) * tv;
        var w11 = tu * tv;
        var wet = water[n00] * w00 + water[n10] * w10 + water[n01] * w01 + water[n11] * w11;
        var depth = deep[n00] * w00 + deep[n10] * w10 + deep[n01] * w01 + deep[n11] * w11;
        var hollowness = hollow[n00] * w00 + hollow[n10] * w10 + hollow[n01] * w01 + hollow[n11] * w11;
        var real = reals[n00] * w00 + reals[n10] * w10 + reals[n01] * w01 + reals[n11] * w11;
        var near = tracks[n00] * w00 + tracks[n10] * w10 + tracks[n01] * w01 + tracks[n11] * w11;

        var firstFamily = families[n00];
        var uniform = true;
        for (var r = -1; r <= 2 && uniform; r++)
        {
          var rowBase = (j + r) * nx + i;
          for (var c = -1; c <= 2; c++)
          {
            if (families[rowBase + c] == firstFamily) continue;
            uniform = false;
            break;
          }
        }
        var mix = 1f;
        int firstCode = biomes[n00];
        var secondCode = firstCode;
        if (!uniform)
        {
          BsplineWeights(tu, spreadU);
          BsplineWeights(tv, spreadV);
          var count = 0;
          for (var r = -1; r <= 2; r++)
          {
            var rowBase = (j + r) * nx + i;
            for (var c = -1; c <= 2; c++)
            {
              var family = families[rowBase + c];
              var weight = spreadU[c + 1] * spreadV[r + 1];
              var found = -1;
              for (var k = 0; k < count; k++)
              {
                if (_familyIds[k] != family) continue;
                found = k;
                break;
              }
              if (found >= 0)
              {
                _familyWeights[found] += weight;
                continue;
              }
              _familyIds[count] = family;
              _familyCodes[count] = biomes[rowBase + c];
              _familyWeights[count] = weight;
              count++;
            }
          }
          var bestScore = float.MinValue;
          var nextScore = float.MinValue;
          var bestCode = -1;
          var nextCode = -1;
          for (var k = 0; k < count; k++)
          {
            var family = _familyIds[k];
            var score = _familyWeights[k] + 0.16f * Perlin(x / 14f + family * 13.1f, z / 14f, 5) + 0.05f * Perlin(x / 3.5f, z / 3.5f, 6 + family);
            if (score > bestScore)
            {
              nextScore = bestScore;
              nextCode = bestCode;
              bestScore = score;
              bestCode = _familyCodes[k];
            }
            else if (score > nextScore)
            {
              nextScore = score;
              nextCode = _familyCodes[k];
            }
          }
          firstCode = bestCode;
          secondCode = nextCode >= 0 ? nextCode : bestCode;
          var margin = nextCode >= 0 ? bestScore - nextScore : 1f;
          mix = 0.5f + 0.5f * Smoothstep(0f, blendWidth, margin);
        }

        Paint(firstCode, x, z, h, shade, slope, hollowness, wet, depth, near, out var red, out var green, out var blue);
        if (mix < 0.999f)
        {
          Paint(secondCode, x, z, h, shade, slope, hollowness, wet, depth, near, out var red2, out var green2, out var blue2);
          red = red * mix + red2 * (1f - mix);
          green = green * mix + green2 * (1f - mix);
          blue = blue * mix + blue2 * (1f - mix);
        }
        if (rivers != null) PaintRiver(rivers, ref red, ref green, ref blue, x, z, mpp, shade);
        if (real < 0.999f)
        {
          var keep = FakeDim + (1f - FakeDim) * real;
          var gray = (red + green + blue) / 3f;
          var saturation = 0.75f + 0.25f * real;
          red = (gray + (red - gray) * saturation) * keep;
          green = (gray + (green - gray) * saturation) * keep;
          blue = (gray + (blue - gray) * saturation) * keep;
        }
        var tone = (red + green + blue) / 3f;
        red = (tone + (red - tone) * Saturation) * Brightness;
        green = (tone + (green - tone) * Saturation) * Brightness;
        blue = (tone + (blue - tone) * Saturation) * Brightness;
        var edge = Math.Min(Math.Min(x - world.X0, right0 - x), Math.Min(z - world.Z0, top0 - z));
        var alpha = Smoothstep(0f, EdgeFade, edge);
        var o = offset + (py * width + px) * 4;
        output[o] = ToByte(red);
        output[o + 1] = ToByte(green);
        output[o + 2] = ToByte(blue);
        output[o + 3] = ToByte(alpha * 255f);
      }
    }
  }

  private void Paint(int code, float x, float z, float h, float shade, float slope, float hollowness, float wet, float depth, float near, out float r, out float g, out float b)
  {
    var f = Family(code);
    var cleared = near < TrackReach && (f == 5 || f == 4) && near < ClearanceAt(x, z);
    if (cleared) f = 3;
    var light = 0.36f + 0.64f * shade;
    var occlusion = Clamp(1f - hollowness * 0.025f, 0.7f, 1.12f);
    var elevation = 0.9f + 0.18f * ((h - World.MinH) / _range);
    if (f == 1)
    {
      Set(Shallow, out r, out g, out b);
      Blend(ref r, ref g, ref b, Middle, Smoothstep(0.35f, 1f, wet));
      Blend(ref r, ref g, ref b, Deep, Smoothstep(0.5f, 1f, depth));
      var ripple = _fine ? 1f + 0.035f * Perlin(x / 6f + z / 40f, z / 9f, 71) : 1f;
      var glint = 0.94f + 0.08f * Math.Min(1.3f, shade);
      r *= ripple * glint;
      g *= ripple * glint;
      b *= ripple * glint;
      return;
    }
    var texture = 1f;
    var ax = x * 0.866f - z * 0.5f;
    var az = x * 0.5f + z * 0.866f;
    if (f == 5)
    {
      Set(Trees, out r, out g, out b);
      Blend(ref r, ref g, ref b, Broadleaf, 0.5f * Noise(x / 90f, z / 90f, 26));
      float canopy;
      if (_fine)
      {
        canopy = 0.6f * Perlin(ax / 8f, az / 8f, 22);
        if (_mpp < 1.6f) canopy += 0.32f * Perlin(z / 3.4f, -x / 3.4f, 24);
        if (_mpp < 0.7f) canopy += 0.18f * Perlin(az / 1.5f, -ax / 1.5f, 25);
      }
      else
      {
        canopy = _medium ? 0.5f * Perlin(ax / 9f, az / 9f, 22) : 0.35f * Perlin(x / 40f, z / 40f, 23);
      }
      if (canopy < 0f) Blend(ref r, ref g, ref b, Understory, Math.Min(0.4f, -canopy * 0.45f));
      texture = (0.96f + 0.16f * canopy) * (1f - 0.08f * Smoothstep(0.6f, 1.2f, slope));
    }
    else if (f == 4)
    {
      Set(Grass, out r, out g, out b);
      if (_fine)
      {
        var patch = Perlin(ax / 4f, az / 4f, 41);
        if (_mpp < 0.7f) patch += 0.4f * Perlin(z / 1.6f, -x / 1.6f, 43);
        var bush = Smoothstep(-0.3f, 0.35f, patch);
        Blend(ref r, ref g, ref b, Shrub, bush);
        texture = 0.97f - 0.1f * bush;
      }
      else
      {
        Blend(ref r, ref g, ref b, Shrub, 0.8f);
        texture = 0.97f + 0.08f * Perlin(x / 8f, z / 8f, 42);
      }
    }
    else if (f == 7)
    {
      const float cell = 140f;
      var fx = Floor(x / cell + Noise(x / 90f, z / 90f, 61) * 0.6f);
      var fz = Floor(z / cell + Noise(x / 90f, z / 90f, 62) * 0.6f);
      var pick = Math.Min(Crops.Length - 1, (int)(Hash(fx, fz, 63) * Crops.Length));
      Set(Crops[pick], out r, out g, out b);
      texture = 1f + 0.05f * Perlin(x / 30f, z / 30f, 64);
      if (_mpp < 0.6f)
      {
        var angle = Hash(fx, fz, 65) * Math.PI;
        texture *= 1f + 0.05f * (float)Math.Sin((x * Math.Cos(angle) + z * Math.Sin(angle)) * Math.PI / 1.8);
      }
    }
    else if (f == 6)
    {
      Set(Wetland, out r, out g, out b);
      texture = 0.99f + 0.1f * Perlin(ax / 5f, az / 5f, 81);
    }
    else if (f == 8)
    {
      Set(Built, out r, out g, out b);
      texture = 0.98f + 0.08f * Perlin(x / 12f, z / 12f, 83) + (_fine ? 0.05f * Perlin(ax / 3f, az / 3f, 82) : 0f);
    }
    else if (f == 2)
    {
      Set(code == 21 || code == 23 ? Gravel : code == 22 ? Dirt : Bare, out r, out g, out b);
      texture = 0.98f + 0.1f * Perlin(x / 10f, z / 10f, 85) + (_mpp < 1f ? 0.05f * Perlin(ax / 1.2f, az / 1.2f, 84) : 0f);
    }
    else if (f == 9)
    {
      Set(Ice, out r, out g, out b);
    }
    else
    {
      Set(cleared ? Mowed : Grass, out r, out g, out b);
      texture = 0.98f + 0.07f * Perlin(x / 25f, z / 25f, 3) + (_fine ? 0.035f * Perlin(az / 3f, -ax / 3f, 4) : 0f);
    }
    if (f != 5 && f != 4) Blend(ref r, ref g, ref b, Rock, 0.55f * Smoothstep(0.55f, 1.1f, slope));
    if (wet > 0.04f) Blend(ref r, ref g, ref b, Mud, Smoothstep(0.04f, 0.4f, wet) * (f == 5 ? 0.25f : 0.6f));
    var total = texture * light * occlusion * elevation * 0.97f;
    r *= total;
    g *= total;
    b *= total;
  }

  private void PaintRiver(TerrainRivers rivers, ref float r, ref float g, ref float b, float x, float z, float mpp, float shade)
  {
    if (!rivers.Query(x, z, mpp * RiverMinPixels, out var sdf, out var half)) return;
    var bank = Math.Max(RiverBank, mpp * 1.5f);
    if (sdf >= bank) return;
    if (sdf > 0f)
    {
      Blend(ref r, ref g, ref b, Mud, 0.35f * (1f - sdf / bank));
      if (sdf >= mpp * 0.5f) return;
    }
    var depth = Clamp(-sdf / Math.Max(1.5f, half * 0.75f), 0f, 1f) * 0.9f;
    var ripple = _fine ? 1f + 0.035f * Perlin(x / 6f + z / 40f, z / 9f, 71) : 1f;
    var glint = (0.94f + 0.08f * Math.Min(1.3f, shade)) * ripple;
    var water = new Rgb((Shallow.R + (Deep.R - Shallow.R) * depth) * glint, (Shallow.G + (Deep.G - Shallow.G) * depth) * glint, (Shallow.B + (Deep.B - Shallow.B) * depth) * glint);
    Blend(ref r, ref g, ref b, water, Clamp(0.5f - sdf / mpp, 0f, 1f));
  }

  private void WaterShare(int nx, int nz, int reach, float[] output)
  {
    var families = _families;
    var rows = _rows;
    for (var j = 0; j < nz; j++)
    {
      for (var i = 0; i < nx; i++)
      {
        var wet = 0;
        for (var di = -reach; di <= reach; di++)
        {
          var ii = Math.Min(nx - 1, Math.Max(0, i + di));
          if (families[j * nx + ii] == 1) wet++;
        }
        rows[j * nx + i] = wet;
      }
    }
    var total = (2f * reach + 1f) * (2f * reach + 1f);
    for (var j = 0; j < nz; j++)
    {
      for (var i = 0; i < nx; i++)
      {
        var sum = 0f;
        for (var dj = -reach; dj <= reach; dj++)
        {
          var jj = Math.Min(nz - 1, Math.Max(0, j + dj));
          sum += rows[jj * nx + i];
        }
        output[j * nx + i] = sum / total;
      }
    }
  }

  private float RealWeight(float x, float z)
  {
    var world = World;
    var tile = world.TileAt(Floor(x / world.Dim), Floor(z / world.Dim));
    if (tile == null) return 0f;
    if (!tile.Edged) return 1f;
    var lx = x - tile.Tx * world.Dim;
    var lz = z - tile.Ty * world.Dim;
    var weight = 1f;
    if (tile.West) weight *= Fade(lx);
    if (tile.East) weight *= Fade(world.Dim - lx);
    if (tile.South) weight *= Fade(lz);
    if (tile.North) weight *= Fade(world.Dim - lz);
    var corners = tile.Corners;
    for (var k = 0; k < corners.Length; k += 2)
    {
      var cx = lx - corners[k];
      var cz = lz - corners[k + 1];
      weight *= Fade((float)Math.Sqrt(cx * cx + cz * cz));
    }
    return weight;
  }

  private float CellHeight(int gx, int gy)
  {
    var world = World;
    var cells = world.Cells;
    var tile = world.TileAt(FloorDiv(gx, cells), FloorDiv(gy, cells));
    if (tile == null) return float.NaN;
    return world.Low + tile.Heights[(gy - tile.Ty * cells) * cells + gx - tile.Tx * cells] * world.Step;
  }

  private float RealHeight(float x, float z)
  {
    var cell = World.Cell;
    var u = x / cell - 0.5f;
    var v = z / cell - 0.5f;
    var gx = Floor(u);
    var gy = Floor(v);
    var fu = u - gx;
    var fv = v - gy;
    var sum = 0f;
    var weights = 0f;
    Accumulate(gx, gy, (1f - fu) * (1f - fv), ref sum, ref weights);
    Accumulate(gx + 1, gy, fu * (1f - fv), ref sum, ref weights);
    Accumulate(gx, gy + 1, (1f - fu) * fv, ref sum, ref weights);
    Accumulate(gx + 1, gy + 1, fu * fv, ref sum, ref weights);
    return weights > 0f ? sum / weights : float.NaN;
  }

  private void Accumulate(int gx, int gy, float weight, ref float sum, ref float weights)
  {
    var h = CellHeight(gx, gy);
    if (float.IsNaN(h)) return;
    sum += h * weight;
    weights += weight;
  }

  private int RealBiome(float x, float z)
  {
    var world = World;
    var cells = world.Cells;
    var gx = Floor(x / world.Cell);
    var gy = Floor(z / world.Cell);
    var tile = world.TileAt(FloorDiv(gx, cells), FloorDiv(gy, cells));
    if (tile == null) return 50;
    return tile.Biome[(gy - tile.Ty * cells) * cells + gx - tile.Tx * cells];
  }

  private float BaseAt(float x, float z)
  {
    var world = World;
    var u = Clamp(x / world.Dim - world.Bx0 - 0.5f, 0f, world.Bnx - 1.001f);
    var v = Clamp(z / world.Dim - world.By0 - 0.5f, 0f, world.Bny - 1.001f);
    var i = (int)u;
    var j = (int)v;
    var fu = u - i;
    var fv = v - j;
    var n = world.Bnx;
    var a = world.Base[j * n + i];
    var b = world.Base[j * n + i + 1];
    var c = world.Base[(j + 1) * n + i];
    var d = world.Base[(j + 1) * n + i + 1];
    return (a * (1f - fu) + b * fu) * (1f - fv) + (c * (1f - fu) + d * fu) * fv;
  }

  private int FakeBiome(float x, float z, float height, float baseHeight)
  {
    var relative = (height - baseHeight) / World.Amplitude;
    if (relative < -0.42f + 0.18f * Perlin(x / 900f, z / 900f, 91)) return Perlin(x / 1200f, z / 1200f, 37) > 0.15f ? 70 : 30;
    if (Perlin(x / 300f, z / 300f, 53) > 0.62f) return 40;
    return 50;
  }

  private static float Ridged(float x, float z)
  {
    var wx = x + 1100f * Perlin(x / 5200f, z / 5200f, 201);
    var wz = z + 1100f * Perlin(x / 5200f + 7.3f, z / 5200f + 2.9f, 202);
    var sum = 0f;
    var amplitude = 0.55f;
    var frequency = 1f / 3400f;
    var weight = 1f;
    var norm = 0f;
    for (var octave = 0; octave < 6; octave++)
    {
      var n = 1f - Math.Abs(Perlin(wx * frequency, wz * frequency, 11 + octave));
      n *= n * weight;
      weight = Clamp(n * 1.7f, 0f, 1f);
      sum += n * amplitude;
      norm += amplitude;
      amplitude *= 0.5f;
      frequency *= 2.1f;
    }
    return sum / norm;
  }

  private static float ClearanceAt(float x, float z) => Clearance + 3f * (Noise(x / 30f, z / 30f, 97) - 0.5f);

  private static int Family(int code) => code == 0 ? 3 : code / 10;

  private static float Fade(float distance) => distance >= Feather ? 1f : Smoothstep(0f, Feather, distance);

  private static void BsplineWeights(float t, float[] output)
  {
    var t2 = t * t;
    var t3 = t2 * t;
    var s = 1f - t;
    output[0] = s * s * s / 6f;
    output[1] = (3f * t3 - 6f * t2 + 4f) / 6f;
    output[2] = (-3f * t3 + 3f * t2 + 3f * t + 1f) / 6f;
    output[3] = t3 / 6f;
  }

  private static float Cubic(float p0, float p1, float p2, float p3, float t)
  {
    return p1 + 0.5f * t * (p2 - p0 + t * (2f * p0 - 5f * p1 + 4f * p2 - p3 + t * (3f * (p1 - p2) + p3 - p0)));
  }

  private static float Bicubic(float[] values, int nx, float u, float v)
  {
    var i = Floor(u);
    var j = Floor(v);
    var tu = u - i;
    var tv = v - j;
    var b0 = (j - 1) * nx + i;
    var b1 = b0 + nx;
    var b2 = b1 + nx;
    var b3 = b2 + nx;
    var r0 = Cubic(values[b0 - 1], values[b0], values[b0 + 1], values[b0 + 2], tu);
    var r1 = Cubic(values[b1 - 1], values[b1], values[b1 + 1], values[b1 + 2], tu);
    var r2 = Cubic(values[b2 - 1], values[b2], values[b2 + 1], values[b2 + 2], tu);
    var r3 = Cubic(values[b3 - 1], values[b3], values[b3 + 1], values[b3 + 2], tu);
    return Cubic(r0, r1, r2, r3, tv);
  }

  private static int HashBits(int ix, int iy, int seed)
  {
    unchecked
    {
      var h = ix * 374761393 + iy * 668265263 + seed * 982451653;
      h = (h ^ (int)((uint)h >> 13)) * 1274126177;
      h ^= (int)((uint)h >> 16);
      return h;
    }
  }

  private static float Hash(int ix, int iy, int seed) => ((uint)HashBits(ix, iy, seed) >> 8) * (1f / 16777216f);

  private static float Noise(float x, float y, int seed)
  {
    var ix = Floor(x);
    var iy = Floor(y);
    var fx = x - ix;
    var fy = y - iy;
    var u = fx * fx * (3f - 2f * fx);
    var v = fy * fy * (3f - 2f * fy);
    var a = Hash(ix, iy, seed);
    var b = Hash(ix + 1, iy, seed);
    var c = Hash(ix, iy + 1, seed);
    var d = Hash(ix + 1, iy + 1, seed);
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
  }

  private static float Perlin(float x, float y, int seed)
  {
    var ix = Floor(x);
    var iy = Floor(y);
    var fx = x - ix;
    var fy = y - iy;
    var u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
    var v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
    var a = Gradient(ix, iy, seed, fx, fy);
    var b = Gradient(ix + 1, iy, seed, fx - 1f, fy);
    var c = Gradient(ix, iy + 1, seed, fx, fy - 1f);
    var d = Gradient(ix + 1, iy + 1, seed, fx - 1f, fy - 1f);
    return (a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v) * 1.4f;
  }

  private static float Gradient(int gx, int gy, int seed, float dx, float dy)
  {
    var index = (int)((uint)HashBits(gx, gy, seed) >> 24);
    return GradX[index] * dx + GradY[index] * dy;
  }

  internal static int Floor(float value)
  {
    var i = (int)value;
    return value < i ? i - 1 : i;
  }

  private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

  private static float Clamp(float value, float low, float high) => value < low ? low : value > high ? high : value;

  private static float Smoothstep(float edge0, float edge1, float value)
  {
    var t = Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
    return t * t * (3f - 2f * t);
  }

  private static byte ToByte(float value) => value <= 0f ? (byte)0 : value >= 255f ? (byte)255 : (byte)(value + 0.5f);

  private static void Set(Rgb color, out float r, out float g, out float b)
  {
    r = color.R;
    g = color.G;
    b = color.B;
  }

  private static void Blend(ref float r, ref float g, ref float b, Rgb color, float amount)
  {
    r += (color.R - r) * amount;
    g += (color.G - g) * amount;
    b += (color.B - b) * amount;
  }

  private static void Ensure<T>(ref T[] buffer, int size)
  {
    if (buffer.Length < size) buffer = new T[size];
  }

  private readonly struct Rgb
  {
    public Rgb(float r, float g, float b)
    {
      R = r;
      G = g;
      B = b;
    }

    public float R { get; }
    public float G { get; }
    public float B { get; }
  }
}
