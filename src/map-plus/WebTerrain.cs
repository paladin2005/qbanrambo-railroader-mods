using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Map.Runtime;
using Map.Runtime.Riff;
using Newtonsoft.Json;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebTerrain
{
  private const int Cells = 100;
  private const int Version = 2;
  private const string Magic = "MPT1";
  private const int HeaderBytes = 24;
  private const float MinimumStep = 0.05f;

  private static readonly object Gate = new();
  private static string _cacheDirectory;
  private static bool _planned;
  private static string _planKey;
  private static Thread _thread;
  private static byte[] _data;
  private static string _key;
  private static int _done;
  private static int _total;
  private static string _error;
  private static bool _errorLogged;

  private sealed class Tile
  {
    public int X;
    public int Y;
    public string Path;
  }

  internal static void Init()
  {
    try
    {
      _cacheDirectory = Path.Combine(Application.temporaryCachePath, "qbanrambo.map-plus");
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Warning($"The web map can't cache the terrain: {exception.Message}");
    }
  }

  internal static void WorldChanged()
  {
    _planned = false;
  }

  internal static byte[] Data
  {
    get
    {
      lock (Gate) return _data;
    }
  }

  internal static string Key
  {
    get
    {
      lock (Gate) return _key;
    }
  }

  internal static string Error
  {
    get
    {
      lock (Gate) return _error;
    }
  }

  internal static void Request()
  {
    if (!_planned) Plan();
  }

  internal static byte[] Status()
  {
    if (!_planned) Plan();
    lock (Gate)
    {
      if (_error != null && !_errorLogged)
      {
        _errorLogged = true;
        MapPlusMod.Log?.Error($"Couldn't read the terrain for the web map: {_error}");
      }
      if (_planKey == null) return Json(new { ok = false, reason = "No terrain loaded" });
      var state = _error != null ? "failed" : _data != null ? "ready" : "building";
      return Json(new { ok = true, state, done = _done, total = _total, key = _key });
    }
  }

  private static void Plan()
  {
    var manager = MapManager.Instance;
    var store = manager != null ? manager.Store : null;
    if (store == null || store.Layers == null || store.Layers.Count == 0) return;
    _planned = true;
    var tiles = new List<Tile>();
    var seen = new HashSet<Vector2Int>();
    foreach (var layer in store.Layers)
    {
      if (layer == null || string.IsNullOrEmpty(layer.BasePath) || layer.BasePath == LayeredMapStore.EmptyPlaceholderBasePath) continue;
      foreach (var position in layer.AllTilePositions)
      {
        if (!seen.Add(position)) continue;
        tiles.Add(new Tile { X = position.x, Y = position.y, Path = Path.Combine(layer.BasePath, $"tile_{position.x:000}_{position.y:000}.riff") });
      }
    }
    if (tiles.Count == 0) return;
    var dimension = store.TileDimension;
    var planKey = dimension + "|" + string.Join("|", tiles.Select(tile => tile.Path));
    lock (Gate)
    {
      if (planKey == _planKey) return;
      _planKey = planKey;
      _data = null;
      _key = null;
      _error = null;
      _errorLogged = false;
      _done = 0;
      _total = tiles.Count;
    }
    var cacheDirectory = _cacheDirectory;
    _thread = new Thread(() => Build(tiles, dimension, cacheDirectory, planKey))
    {
      IsBackground = true,
      Priority = System.Threading.ThreadPriority.BelowNormal,
      Name = "Map Plus terrain"
    };
    _thread.Start();
  }

  private static void Build(List<Tile> tiles, int dimension, string cacheDirectory, string planKey)
  {
    try
    {
      var key = KeyFor(tiles, dimension);
      var cachePath = cacheDirectory != null ? Path.Combine(cacheDirectory, $"terrain-{key}.bin") : null;
      if (cachePath != null && File.Exists(cachePath))
      {
        var cached = File.ReadAllBytes(cachePath);
        if (cached.Length > HeaderBytes && cached[0] == 0x1f && cached[1] == 0x8b)
        {
          Finish(planKey, cached, key, tiles.Count);
          return;
        }
      }
      var read = new List<(Tile Tile, float[] Heights, byte[] Biome)>();
      var low = float.MaxValue;
      var high = float.MinValue;
      foreach (var tile in tiles)
      {
        lock (Gate)
        {
          if (_planKey != planKey) return;
        }
        if (TryRead(tile, out var heights, out var biome))
        {
          read.Add((tile, heights, biome));
          foreach (var value in heights)
          {
            if (value < low) low = value;
            if (value > high) high = value;
          }
        }
        Interlocked.Increment(ref _done);
      }
      if (read.Count == 0) throw new InvalidDataException("no terrain tile could be read");
      var step = Mathf.Max(MinimumStep, (high - low) / 65000f);
      var data = Compress(Encode(read, dimension, low, step));
      if (cachePath != null) Save(cacheDirectory, cachePath, data);
      Finish(planKey, data, key, tiles.Count);
    }
    catch (Exception exception)
    {
      lock (Gate)
      {
        if (_planKey == planKey) _error = exception.Message;
      }
    }
  }

  private static void Finish(string planKey, byte[] data, string key, int total)
  {
    lock (Gate)
    {
      if (_planKey != planKey) return;
      _data = data;
      _key = key;
      _done = total;
    }
  }

  private static string KeyFor(List<Tile> tiles, int dimension)
  {
    var text = new StringBuilder();
    text.Append(Magic).Append('|').Append(Version).Append('|').Append(Cells).Append('|').Append(dimension);
    foreach (var tile in tiles)
    {
      var info = new FileInfo(tile.Path);
      text.Append('|').Append(tile.X).Append(',').Append(tile.Y).Append(',').Append(tile.Path);
      if (info.Exists) text.Append(',').Append(info.Length).Append(',').Append(info.LastWriteTimeUtc.Ticks);
    }
    using var sha = SHA1.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
    return string.Concat(hash.Take(8).Select(value => value.ToString("x2")));
  }

  private static bool TryRead(Tile tile, out float[] heights, out byte[] biome)
  {
    heights = null;
    biome = null;
    if (!File.Exists(tile.Path)) return false;
    var reader = new RiffReader(File.ReadAllBytes(tile.Path));
    if (reader.FormatId != "RRT0") return false;
    var buffer = new byte[0];
    var hasRange = false;
    float low = 0f, high = 0f;
    if (reader.TryReadData("RHGI", ref buffer, out var count) && count >= 12)
    {
      hasRange = true;
      low = BitConverter.ToInt32(buffer, 4);
      high = BitConverter.ToInt32(buffer, 8);
    }
    if (!reader.TryReadData("RHGT", ref buffer, out count) || count < 12) return false;
    var format = Encoding.ASCII.GetString(buffer, 0, 4);
    var size = BitConverter.ToInt32(buffer, 4);
    if (size < 2 || BitConverter.ToInt32(buffer, 8) != size) return false;
    var wide = format == "RFLT";
    if (wide ? count < 12 + size * size * 4 : format != "R16 " || !hasRange || count < 12 + size * size * 2) return false;
    var source = new float[size * size];
    for (var i = 0; i < source.Length; i++)
    {
      source[i] = wide ? BitConverter.ToSingle(buffer, 12 + i * 4) : BitConverter.ToUInt16(buffer, 12 + i * 2) / 65535f * (high - low) + low;
    }
    heights = new float[Cells * Cells];
    var last = size - 1;
    for (var row = 0; row < Cells; row++)
    {
      var v = (row + 0.5f) / Cells * last;
      var y0 = Mathf.Min((int)v, last - 1);
      var fy = v - y0;
      for (var column = 0; column < Cells; column++)
      {
        var u = (column + 0.5f) / Cells * last;
        var x0 = Mathf.Min((int)u, last - 1);
        var fx = u - x0;
        var a = source[y0 * size + x0];
        var b = source[y0 * size + x0 + 1];
        var c = source[(y0 + 1) * size + x0];
        var d = source[(y0 + 1) * size + x0 + 1];
        heights[row * Cells + column] = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
      }
    }
    biome = new byte[Cells * Cells];
    if (reader.TryReadData("RBIO", ref buffer, out count) && count >= 12 && Encoding.ASCII.GetString(buffer, 0, 4) == "R8  ")
    {
      var width = BitConverter.ToInt32(buffer, 4);
      var height = BitConverter.ToInt32(buffer, 8);
      if (width > 0 && height > 0 && count >= 12 + width * height)
      {
        for (var row = 0; row < Cells; row++)
        {
          var y = Mathf.Min((int)((row + 0.5f) / Cells * height), height - 1);
          for (var column = 0; column < Cells; column++)
          {
            var x = Mathf.Min((int)((column + 0.5f) / Cells * width), width - 1);
            biome[row * Cells + column] = buffer[12 + y * width + x];
          }
        }
      }
    }
    return true;
  }

  private static byte[] Encode(List<(Tile Tile, float[] Heights, byte[] Biome)> tiles, int dimension, float low, float step)
  {
    using var stream = new MemoryStream(HeaderBytes + tiles.Count * (8 + Cells * Cells * 3));
    using var writer = new BinaryWriter(stream);
    writer.Write(Encoding.ASCII.GetBytes(Magic));
    writer.Write(dimension);
    writer.Write(Cells);
    writer.Write(tiles.Count);
    writer.Write(low);
    writer.Write(step);
    foreach (var (tile, heights, biome) in tiles)
    {
      writer.Write(tile.X);
      writer.Write(tile.Y);
      foreach (var value in heights) writer.Write((ushort)Mathf.Clamp(Mathf.RoundToInt((value - low) / step), 0, 65535));
      writer.Write(biome);
    }
    writer.Flush();
    return stream.ToArray();
  }

  private static byte[] Compress(byte[] data)
  {
    using var output = new MemoryStream(data.Length / 2);
    using (var gzip = new GZipStream(output, CompressionMode.Compress))
    {
      gzip.Write(data, 0, data.Length);
    }
    return output.ToArray();
  }

  private static void Save(string directory, string path, byte[] data)
  {
    try
    {
      Directory.CreateDirectory(directory);
      foreach (var old in Directory.GetFiles(directory, "terrain-*.bin"))
      {
        if (old != path) File.Delete(old);
      }
      var temporary = path + ".tmp";
      File.WriteAllBytes(temporary, data);
      if (File.Exists(path)) File.Delete(path);
      File.Move(temporary, path);
    }
    catch (Exception)
    {
    }
  }

  private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
}
