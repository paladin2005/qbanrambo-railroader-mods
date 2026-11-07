using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Helpers;
using Track;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class MapTerrain : MonoBehaviour
{
  private const int ChunkPixels = 512;
  private const float OverviewMpp = 20f;
  private const int OverviewStrip = 48;
  private const float Sharpness = 1.4f;
  private const int MaxChunks = 72;
  private const int UploadsPerFrame = 2;
  private const float ViewMargin = 0.15f;
  private const float PlaneDrop = 100f;
  private const float TrackCheckSeconds = 10f;
  private const float RequestSeconds = 1f;
  private const int PooledBuffers = 8;
  private const int SortingOrder = -100;
  private const int StatsAfter = 24;

  private static readonly float[] Spans = { 4096f, 2048f, 1024f, 512f, 256f, 128f };

  private readonly object _gate = new();
  private readonly List<Job> _queue = new();
  private readonly Queue<Job> _done = new();
  private readonly HashSet<long> _pending = new();
  private readonly Stack<byte[]> _buffers = new();
  private readonly List<Thread> _threads = new();
  private readonly Dictionary<long, Chunk> _chunks = new();

  private volatile bool _stopping;
  private TerrainWorld _world;
  private TerrainRails _rails;
  private TerrainRivers _rivers;
  private TerrainRivers _loadedRivers;
  private int _generation;
  private bool _loading;
  private bool _failed;
  private TerrainWorld _loadedWorld;
  private TerrainRails _loadedRails;
  private string _loadError;
  private float _nextRequest;
  private int _trackSignature;
  private float _nextTrackCheck;
  private bool _railsBuilding;

  private GameObject _root;
  private RectTransform[] _levels;
  private RawImage _overview;
  private Texture2D _overviewTexture;
  private byte[] _overviewPixels;
  private int _overviewWidth;
  private int _overviewHeight;
  private float _overviewScale = float.MaxValue;
  private int _stripsLeft;
  private Stopwatch _overviewClock;
  private int _lastLevel = -2;
  private Vector4 _lastView;
  private bool _dirty;
  private bool _logged;
  private bool _paintLogged;
  private int _painted;
  private double _paintedMilliseconds;

  private void Update()
  {
    try
    {
      var shown = MapMouse.IsMapShown(out var camera) && MapPlusMod.Terrain.IsOn;
      if (_root != null && _root.activeSelf != shown) _root.SetActive(shown);
      if (!shown)
      {
        DropChunkJobs();
        return;
      }
      if (_world == null)
      {
        Prepare();
        return;
      }
      Collect();
      CheckTrack();
      Place(camera);
      Plan(camera);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the terrain on the map");
    }
  }

  private void OnDestroy()
  {
    _stopping = true;
    lock (_gate)
    {
      _queue.Clear();
      _pending.Clear();
      Monitor.PulseAll(_gate);
    }
    foreach (var chunk in _chunks.Values) DestroyChunk(chunk);
    _chunks.Clear();
    if (_overviewTexture != null) Destroy(_overviewTexture);
    if (_root != null) Destroy(_root);
  }

  private void Prepare()
  {
    if (_failed) return;
    if (_loading)
    {
      TerrainWorld world;
      TerrainRails rails;
      TerrainRivers rivers;
      string error;
      lock (_gate)
      {
        world = _loadedWorld;
        rails = _loadedRails;
        rivers = _loadedRivers;
        error = _loadError;
        _loadedWorld = null;
        _loadedRails = null;
        _loadedRivers = null;
        _loadError = null;
      }
      if (error != null)
      {
        _failed = true;
        _loading = false;
        MapPlusMod.Log?.Error($"Couldn't load the terrain for the map: {error}");
        return;
      }
      if (world == null) return;
      _loading = false;
      _world = world;
      _rails = rails;
      _rivers = rivers;
      _nextTrackCheck = Time.unscaledTime + TrackCheckSeconds;
      Build();
      StartWorkers();
      QueueOverview();
      return;
    }
    if (Time.unscaledTime < _nextRequest) return;
    _nextRequest = Time.unscaledTime + RequestSeconds;
    WebTerrain.Request();
    var buildError = WebTerrain.Error;
    if (buildError != null)
    {
      _failed = true;
      MapPlusMod.Log?.Error($"Couldn't read the terrain for the map: {buildError}");
      return;
    }
    var data = WebTerrain.Data;
    if (data == null) return;
    _loading = true;
    var segments = TrackSegments();
    var riverPaths = WebRivers.Paths();
    _trackSignature = TrackSignature();
    Background("Map Plus terrain loader", () =>
    {
      try
      {
        var world = TerrainWorld.Load(data);
        var rails = TerrainRails.Build(segments);
        var rivers = TerrainRivers.Build(riverPaths);
        lock (_gate)
        {
          _loadedWorld = world;
          _loadedRails = rails;
          _loadedRivers = rivers;
        }
      }
      catch (Exception exception)
      {
        lock (_gate) _loadError = exception.Message;
      }
    });
  }

  private void CheckTrack()
  {
    if (_railsBuilding)
    {
      TerrainRails rails;
      lock (_gate)
      {
        rails = _loadedRails;
        _loadedRails = null;
      }
      if (rails == null) return;
      _railsBuilding = false;
      _rails = rails;
      _generation++;
      ClearChunks();
      return;
    }
    if (Time.unscaledTime < _nextTrackCheck) return;
    _nextTrackCheck = Time.unscaledTime + TrackCheckSeconds;
    var signature = TrackSignature();
    if (signature == _trackSignature) return;
    _trackSignature = signature;
    var segments = TrackSegments();
    _railsBuilding = true;
    Background("Map Plus terrain tracks", () =>
    {
      var rails = TerrainRails.Build(segments);
      lock (_gate) _loadedRails = rails;
    });
  }

  private static void Background(string name, ThreadStart work)
  {
    new Thread(work) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = name }.Start();
  }

  private static List<float[]> TrackSegments()
  {
    var segments = new List<float[]>();
    var graph = Graph.Shared;
    if (graph == null) return segments;
    foreach (var segment in graph.Segments)
    {
      if (!Clears(segment)) continue;
      var a = segment.a.Position;
      var b = segment.b.Position;
      TrackSegment.ComputeBezierHandles(a, segment.a.Rotation, b, segment.b.Rotation, out var p1, out var p2);
      segments.Add(new[] { a.x, a.z, p1.x, p1.z, p2.x, p2.z, b.x, b.z });
    }
    return segments;
  }

  private static int TrackSignature()
  {
    var graph = Graph.Shared;
    if (graph == null) return 0;
    var hash = 17;
    unchecked
    {
      foreach (var segment in graph.Segments)
      {
        if (!Clears(segment)) continue;
        hash = hash * 31 + Mathf.RoundToInt(segment.a.Position.x);
        hash = hash * 31 + Mathf.RoundToInt(segment.b.Position.z);
      }
    }
    return hash;
  }

  private static bool Clears(TrackSegment segment)
  {
    return segment?.a != null && segment.b != null && (segment.flags & TrackSegment.Flags.Tunnel) == 0 && TrackInfo.IsBuilt(segment);
  }

  private void Build()
  {
    _root = new GameObject("Map Plus Terrain", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    canvas.sortingOrder = SortingOrder;
    var overviewObject = new GameObject("Overview", typeof(RectTransform)) { layer = Layers.Map };
    overviewObject.transform.SetParent(_root.transform, false);
    _overview = overviewObject.AddComponent<RawImage>();
    _overview.raycastTarget = false;
    _overview.enabled = false;
    _levels = new RectTransform[Spans.Length];
    for (var level = 0; level < Spans.Length; level++)
    {
      var levelObject = new GameObject($"Level {level}", typeof(RectTransform)) { layer = Layers.Map };
      levelObject.transform.SetParent(_root.transform, false);
      _levels[level] = (RectTransform)levelObject.transform;
    }
  }

  private void StartWorkers()
  {
    if (_threads.Count > 0) return;
    var count = Mathf.Clamp(SystemInfo.processorCount / 2, 1, 4);
    for (var k = 0; k < count; k++)
    {
      var thread = new Thread(Work) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "Map Plus terrain painter" };
      _threads.Add(thread);
      thread.Start();
    }
  }

  private void QueueOverview()
  {
    var world = _world;
    var largest = Mathf.Min(8192, SystemInfo.maxTextureSize);
    var mpp = Mathf.Max(OverviewMpp, Mathf.Max(world.Width, world.Height) / largest);
    _overviewScale = mpp;
    _overviewWidth = Mathf.CeilToInt(world.Width / mpp);
    _overviewHeight = Mathf.CeilToInt(world.Height / mpp);
    _overviewPixels = new byte[_overviewWidth * _overviewHeight * 4];
    _stripsLeft = 0;
    lock (_gate)
    {
      for (var row = 0; row < _overviewHeight; row += OverviewStrip)
      {
        _queue.Add(new Job
        {
          Overview = true,
          Level = -1,
          World = world,
          X0 = world.X0,
          Z0 = world.Z0 + row * mpp,
          Mpp = mpp,
          Grid = Mathf.Max(world.Cell, mpp),
          Width = _overviewWidth,
          Height = Math.Min(OverviewStrip, _overviewHeight - row),
          Rivers = _rivers,
          Pixels = _overviewPixels,
          Offset = row * _overviewWidth * 4,
          Priority = -1e9f + row
        });
        _stripsLeft++;
      }
      Monitor.PulseAll(_gate);
    }
    _overviewClock = Stopwatch.StartNew();
  }

  private void Work()
  {
    TerrainPainter painter = null;
    var clock = new Stopwatch();
    while (true)
    {
      Job job;
      lock (_gate)
      {
        while (!_stopping && _queue.Count == 0) Monitor.Wait(_gate);
        if (_stopping) return;
        var best = 0;
        for (var k = 1; k < _queue.Count; k++)
        {
          if (_queue[k].Priority < _queue[best].Priority) best = k;
        }
        job = _queue[best];
        _queue.RemoveAt(best);
        if (job.Pixels == null && _buffers.Count > 0) job.Pixels = _buffers.Pop();
      }
      try
      {
        job.Pixels ??= new byte[job.Width * job.Height * 4];
        if (painter == null || painter.World != job.World) painter = new TerrainPainter(job.World);
        clock.Reset();
        clock.Start();
        painter.Render(job.X0, job.Z0, job.Mpp, job.Grid, job.Width, job.Height, job.Rails, job.Rivers, job.Pixels, job.Offset);
        job.Milliseconds = clock.Elapsed.TotalMilliseconds;
      }
      catch (Exception exception)
      {
        job.Error = exception.ToString();
      }
      lock (_gate) _done.Enqueue(job);
    }
  }

  private void Collect()
  {
    var uploads = 0;
    while (uploads < UploadsPerFrame)
    {
      Job job;
      lock (_gate)
      {
        if (_done.Count == 0) break;
        job = _done.Dequeue();
        if (!job.Overview && job.Generation == _generation) _pending.Remove(job.Key);
      }
      if (job.Error != null && !_paintLogged)
      {
        _paintLogged = true;
        MapPlusMod.Log?.Error($"Couldn't paint the map terrain: {job.Error}");
      }
      if (job.Overview)
      {
        _stripsLeft--;
        if (_stripsLeft > 0) continue;
        CreateOverview();
        uploads++;
        continue;
      }
      if (job.Error == null && job.Generation == _generation && job.World == _world)
      {
        AddChunk(job);
        uploads++;
        Count(job);
      }
      Recycle(job.Pixels);
    }
  }

  private void Count(Job job)
  {
    _painted++;
    _paintedMilliseconds += job.Milliseconds;
    if (_painted != StatsAfter) return;
    MapPlusMod.Log?.Info($"Map Plus terrain: {_paintedMilliseconds / _painted:0} ms a chunk on {_threads.Count} threads");
  }

  private void Recycle(byte[] pixels)
  {
    if (pixels == null || pixels.Length != ChunkPixels * ChunkPixels * 4) return;
    lock (_gate)
    {
      if (_buffers.Count < PooledBuffers) _buffers.Push(pixels);
    }
  }

  private void CreateOverview()
  {
    if (_overviewPixels == null) return;
    var texture = NewTexture(_overviewWidth, _overviewHeight, "Map Plus terrain overview");
    texture.SetPixelData(_overviewPixels, 0);
    texture.Apply(true, true);
    _overviewPixels = null;
    _overviewTexture = texture;
    _overview.texture = texture;
    var rect = _overview.rectTransform;
    rect.pivot = Vector2.zero;
    rect.sizeDelta = new Vector2(_overviewWidth * _overviewScale, _overviewHeight * _overviewScale);
    rect.localPosition = new Vector3(_world.X0, _world.Z0, 0f);
    _overview.enabled = true;
    var seconds = _overviewClock != null ? _overviewClock.Elapsed.TotalSeconds : 0;
    MapPlusMod.Log?.Info($"Map Plus painted the map terrain ({_world.TileCount} tiles, {_overviewWidth}x{_overviewHeight}) in {seconds:0.0} s");
  }

  private void AddChunk(Job job)
  {
    if (_chunks.TryGetValue(job.Key, out var old)) DestroyChunk(old);
    var span = Spans[job.Level];
    var texture = NewTexture(ChunkPixels, ChunkPixels, "Map Plus terrain");
    texture.SetPixelData(job.Pixels, 0);
    texture.Apply(true, true);
    var chunkObject = new GameObject("Terrain", typeof(RectTransform)) { layer = Layers.Map };
    var rect = (RectTransform)chunkObject.transform;
    rect.SetParent(_levels[job.Level], false);
    rect.pivot = Vector2.zero;
    rect.sizeDelta = new Vector2(span, span);
    rect.localPosition = new Vector3(job.X0, job.Z0, 0f);
    var image = chunkObject.AddComponent<RawImage>();
    image.texture = texture;
    image.raycastTarget = false;
    _chunks[job.Key] = new Chunk { Level = job.Level, X0 = job.X0, Z0 = job.Z0, Span = span, Texture = texture, Image = image, LastUsed = Time.unscaledTime };
    _dirty = true;
  }

  private static Texture2D NewTexture(int width, int height, string name)
  {
    return new Texture2D(width, height, TextureFormat.RGBA32, true)
    {
      name = name,
      wrapMode = TextureWrapMode.Clamp,
      filterMode = FilterMode.Trilinear,
      anisoLevel = 1
    };
  }

  private void DestroyChunk(Chunk chunk)
  {
    if (chunk.Image != null) Destroy(chunk.Image.gameObject);
    if (chunk.Texture != null) Destroy(chunk.Texture);
  }

  private void ClearChunks()
  {
    foreach (var chunk in _chunks.Values) DestroyChunk(chunk);
    _chunks.Clear();
    lock (_gate)
    {
      _queue.RemoveAll(job => !job.Overview);
      _pending.Clear();
    }
    _dirty = true;
  }

  private void DropChunkJobs()
  {
    _lastLevel = -2;
    lock (_gate)
    {
      if (_pending.Count == 0) return;
      for (var k = _queue.Count - 1; k >= 0; k--)
      {
        var job = _queue[k];
        if (job.Overview) continue;
        _queue.RemoveAt(k);
        _pending.Remove(job.Key);
      }
    }
  }

  private void Place(Camera camera)
  {
    var cameraGame = WorldTransformer.WorldToGame(camera.transform.position);
    var plane = Mathf.Max(_world.MinH - PlaneDrop, cameraGame.y - camera.farClipPlane * 0.95f);
    _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(new Vector3(0f, plane, 0f)), Quaternion.Euler(90f, 0f, 0f));
  }

  private void Plan(Camera camera)
  {
    var center = WorldTransformer.WorldToGame(camera.transform.position);
    var halfHeight = camera.orthographicSize;
    var halfWidth = halfHeight * camera.aspect;
    var screenMpp = 2f * halfHeight / Mathf.Max(1, camera.pixelHeight);
    var level = LevelFor(screenMpp);
    var view = new Vector4(center.x - halfWidth, center.z - halfHeight, center.x + halfWidth, center.z + halfHeight);
    var moved = Mathf.Abs(view.x - _lastView.x) + Mathf.Abs(view.y - _lastView.y) + Mathf.Abs(view.z - _lastView.z) + Mathf.Abs(view.w - _lastView.w);
    if (level == _lastLevel && moved < halfHeight * 0.05f && !_dirty) return;
    _dirty = false;
    _lastLevel = level;
    _lastView = view;
    var margin = halfHeight * 2f * ViewMargin;
    var padded = new Vector4(view.x - margin, view.y - margin, view.z + margin, view.w + margin);
    var now = Time.unscaledTime;
    foreach (var chunk in _chunks.Values)
    {
      var visible = chunk.Level <= level && Intersects(chunk, padded);
      if (visible) chunk.LastUsed = now;
      if (chunk.Image.gameObject.activeSelf != visible) chunk.Image.gameObject.SetActive(visible);
    }
    Request(level, view, padded, center);
    Evict();
  }

  private int LevelFor(float screenMpp)
  {
    var target = screenMpp * Sharpness;
    if (_overviewScale <= target) return -1;
    for (var level = 0; level < Spans.Length; level++)
    {
      if (Spans[level] / ChunkPixels <= target) return level;
    }
    return Spans.Length - 1;
  }

  private void Request(int level, Vector4 view, Vector4 padded, Vector3 center)
  {
    var wanted = new List<Job>();
    var keys = new HashSet<long>();
    if (level >= 2) Wanted(level - 2, view, center, 0f, wanted, keys);
    if (level >= 0) Wanted(level, padded, center, 1e7f, wanted, keys);
    lock (_gate)
    {
      var queued = new Dictionary<long, Job>();
      for (var k = _queue.Count - 1; k >= 0; k--)
      {
        var job = _queue[k];
        if (job.Overview) continue;
        if (keys.Contains(job.Key))
        {
          queued[job.Key] = job;
          continue;
        }
        _queue.RemoveAt(k);
        _pending.Remove(job.Key);
      }
      foreach (var job in wanted)
      {
        if (queued.TryGetValue(job.Key, out var existing))
        {
          existing.Priority = job.Priority;
          continue;
        }
        if (!_pending.Add(job.Key)) continue;
        _queue.Add(job);
      }
      Monitor.PulseAll(_gate);
    }
  }

  private void Wanted(int level, Vector4 box, Vector3 center, float basePriority, List<Job> wanted, HashSet<long> keys)
  {
    var world = _world;
    var span = Spans[level];
    var columns = Mathf.CeilToInt(world.Width / span);
    var rows = Mathf.CeilToInt(world.Height / span);
    var i0 = Mathf.Max(0, Mathf.FloorToInt((box.x - world.X0) / span));
    var i1 = Mathf.Min(columns - 1, Mathf.FloorToInt((box.z - world.X0) / span));
    var j0 = Mathf.Max(0, Mathf.FloorToInt((box.y - world.Z0) / span));
    var j1 = Mathf.Min(rows - 1, Mathf.FloorToInt((box.w - world.Z0) / span));
    var mpp = span / ChunkPixels;
    for (var j = j0; j <= j1; j++)
    {
      for (var i = i0; i <= i1; i++)
      {
        var key = ((long)level << 48) | ((long)i << 24) | (long)j;
        keys.Add(key);
        if (_chunks.ContainsKey(key)) continue;
        var x0 = world.X0 + i * span;
        var z0 = world.Z0 + j * span;
        var dx = x0 + span / 2f - center.x;
        var dz = z0 + span / 2f - center.z;
        wanted.Add(new Job
        {
          Key = key,
          Level = level,
          Generation = _generation,
          World = world,
          Rails = _rails,
          Rivers = _rivers,
          X0 = x0,
          Z0 = z0,
          Mpp = mpp,
          Grid = Mathf.Max(world.Cell, mpp),
          Width = ChunkPixels,
          Height = ChunkPixels,
          Priority = basePriority + Mathf.Sqrt(dx * dx + dz * dz)
        });
      }
    }
  }

  private void Evict()
  {
    if (_chunks.Count <= MaxChunks) return;
    var stale = _chunks
      .Where(entry => !entry.Value.Image.gameObject.activeSelf)
      .OrderBy(entry => entry.Value.LastUsed)
      .Take(_chunks.Count - MaxChunks)
      .Select(entry => entry.Key)
      .ToList();
    foreach (var key in stale)
    {
      DestroyChunk(_chunks[key]);
      _chunks.Remove(key);
    }
  }

  private static bool Intersects(Chunk chunk, Vector4 box)
  {
    return chunk.X0 < box.z && chunk.X0 + chunk.Span > box.x && chunk.Z0 < box.w && chunk.Z0 + chunk.Span > box.y;
  }

  private sealed class Job
  {
    public long Key;
    public int Level;
    public int Generation;
    public bool Overview;
    public TerrainWorld World;
    public TerrainRails Rails;
    public TerrainRivers Rivers;
    public float X0;
    public float Z0;
    public float Mpp;
    public float Grid;
    public int Width;
    public int Height;
    public byte[] Pixels;
    public int Offset;
    public float Priority;
    public string Error;
    public double Milliseconds;
  }

  private sealed class Chunk
  {
    public int Level;
    public float X0;
    public float Z0;
    public float Span;
    public Texture2D Texture;
    public RawImage Image;
    public float LastUsed;
  }
}
