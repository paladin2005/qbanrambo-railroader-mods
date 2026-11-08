using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Track;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class HighlightOverlay : MonoBehaviour
{
  private const float LineHeight = 55f;
  private const float SampleStep = 5f;
  private const float GlowFraction = 0.055f;
  private const float CoreFraction = 0.016f;
  private const float PulsePeriod = 1.2f;

  private GameObject _root;
  private GlowLine _glow;
  private GlowLine _core;
  private int _version = -1;
  private Vector3 _center;
  private Color _color;
  private float _nextPulse;
  private bool _visible;
  private bool _logged;

  private void OnDestroy()
  {
    if (_root != null) Destroy(_root);
  }

  private void Update()
  {
    try
    {
      var builder = MapBuilder.Shared;
      var camera = builder != null ? builder.mapCamera : null;
      if (camera == null || !camera.gameObject.activeInHierarchy || !SpanHighlights.Active)
      {
        Show(false);
        _version = -1;
        return;
      }
      if (_version != SpanHighlights.Version)
      {
        _version = SpanHighlights.Version;
        Rebuild();
      }
      if (!_visible) return;
      _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(Vector3.zero), Quaternion.Euler(90f, 0f, 0f));
      var size = camera.orthographicSize;
      var beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / PulsePeriod);
      _glow.Configure(size * GlowFraction * (0.85f + 0.45f * beat));
      _glow.color = new Color(_color.r, _color.g, _color.b, 0.45f + 0.35f * beat);
      _core.Configure(size * CoreFraction);
      if (Time.unscaledTime < _nextPulse) return;
      _nextPulse = Time.unscaledTime + PulsePeriod;
      MapPulse.Show(_center, _color);
    }
    catch (Exception exception)
    {
      Show(false);
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't highlight the destination on the map");
    }
  }

  private void Rebuild()
  {
    var segments = SpanHighlights.Segments;
    if (segments.Count == 0)
    {
      Show(false);
      return;
    }
    Build();
    var lines = new List<List<Vector3>>();
    foreach (var segment in segments)
    {
      var length = segment.GetLength();
      var steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleStep));
      var line = new List<Vector3>(steps + 1);
      for (var i = 0; i <= steps; i++)
      {
        segment.GetPositionRotationAtDistance(length * i / steps, TrackSegment.End.A, PositionAccuracy.Standard, out var point, out _);
        line.Add(new Vector3(point.x, point.z, -(point.y + LineHeight)));
      }
      lines.Add(line);
    }
    _glow.SetLines(lines);
    _core.SetLines(lines);
    _color = SpanHighlights.HighlightColor;
    _core.color = Color.Lerp(_color, Color.white, 0.7f);
    _center = SpanHighlights.Center(segments);
    _nextPulse = 0f;
    Show(true);
  }

  private void Show(bool visible)
  {
    _visible = visible;
    if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
  }

  private void Build()
  {
    if (_root != null) return;
    _root = new GameObject("Map Plus Highlight", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    if (prefabCanvas != null) canvas.sortingLayerID = prefabCanvas.sortingLayerID;
    canvas.sortingOrder = prefabCanvas != null ? prefabCanvas.sortingOrder : 0;
    _glow = AddLine("Glow");
    _core = AddLine("Core");
  }

  private GlowLine AddLine(string name)
  {
    var lineObject = new GameObject(name, typeof(RectTransform)) { layer = Layers.Map };
    lineObject.transform.SetParent(_root.transform, false);
    var line = lineObject.AddComponent<GlowLine>();
    line.raycastTarget = false;
    return line;
  }
}

internal sealed class GlowLine : MaskableGraphic
{
  private const int TextureWidth = 4;
  private const int TextureHeight = 64;

  private static Texture2D _soft;

  private readonly List<List<Vector3>> _lines = new();
  private float _width = 1f;

  public override Texture mainTexture => Soft;

  internal void SetLines(List<List<Vector3>> lines)
  {
    _lines.Clear();
    _lines.AddRange(lines);
    SetVerticesDirty();
  }

  internal void Configure(float width)
  {
    _width = Mathf.Max(0.01f, width);
    SetVerticesDirty();
  }

  protected override void OnPopulateMesh(VertexHelper helper)
  {
    helper.Clear();
    var half = _width / 2f;
    foreach (var line in _lines)
    {
      if (line.Count < 2) continue;
      var start = helper.currentVertCount;
      for (var i = 0; i < line.Count; i++)
      {
        var point = line[i];
        var previous = i > 0 ? Flat(point) - Flat(line[i - 1]) : Vector2.zero;
        var next = i < line.Count - 1 ? Flat(line[i + 1]) - Flat(point) : Vector2.zero;
        var tangent = previous.normalized + next.normalized;
        if (tangent.sqrMagnitude < 0.0001f) tangent = next.sqrMagnitude > 0f ? next : previous;
        tangent.Normalize();
        var offset = new Vector3(-tangent.y, tangent.x, 0f) * half;
        helper.AddVert(point + offset, color, new Vector2(0.5f, 1f));
        helper.AddVert(point - offset, color, new Vector2(0.5f, 0f));
        if (i == 0) continue;
        var vertex = start + i * 2;
        helper.AddTriangle(vertex - 2, vertex - 1, vertex);
        helper.AddTriangle(vertex - 1, vertex + 1, vertex);
      }
    }
  }

  private static Vector2 Flat(Vector3 point) => new(point.x, point.y);

  private static Texture2D Soft => _soft != null ? _soft : _soft = CreateSoft();

  private static Texture2D CreateSoft()
  {
    var texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
    {
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSave
    };
    var pixels = new Color32[TextureWidth * TextureHeight];
    for (var y = 0; y < TextureHeight; y++)
    {
      var across = Mathf.Abs((y + 0.5f) / TextureHeight * 2f - 1f);
      var alpha = (byte)Mathf.RoundToInt(255f * Mathf.Pow(Mathf.Clamp01(1f - across), 1.6f));
      for (var x = 0; x < TextureWidth; x++) pixels[y * TextureWidth + x] = new Color32(255, 255, 255, alpha);
    }
    texture.SetPixels32(pixels);
    texture.Apply();
    return texture;
  }
}
