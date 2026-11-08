using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class PathOverlay : MonoBehaviour
{
  private const float LineHeight = 52f;
  private const float WidthFraction = 0.016f;
  private const float CasingFraction = 0.024f;
  private const float MinimumWidth = 3.4f;
  private const float TintAlpha = 0.85f;

  private static readonly Color CasingColor = new(0.02f, 0.11f, 0.14f, 0.85f);

  private readonly Dictionary<Color, StripLine> _lines = new();
  private GameObject _root;
  private StripLine _casing;
  private int _version = -1;
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
      if (camera == null || !camera.gameObject.activeInHierarchy || !SpanHighlights.PathsActive)
      {
        Show(false);
        _version = -1;
        return;
      }
      if (_version != SpanHighlights.PathVersion)
      {
        _version = SpanHighlights.PathVersion;
        Rebuild();
      }
      if (!_visible) return;
      _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(Vector3.zero), Quaternion.Euler(90f, 0f, 0f));
      var size = camera.orthographicSize;
      var width = Mathf.Max(MinimumWidth, size * WidthFraction);
      _casing.Configure(Mathf.Max(MinimumWidth * 1.5f, size * CasingFraction));
      foreach (var line in _lines.Values) line.Configure(width);
    }
    catch (Exception exception)
    {
      Show(false);
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the highlighted path on the map");
    }
  }

  private void Rebuild()
  {
    var paths = SpanHighlights.Paths.Where(path => path.Points.Count >= 2).ToList();
    if (paths.Count == 0)
    {
      Show(false);
      return;
    }
    Build();
    var all = new List<List<Vector3>>();
    var byTint = new Dictionary<Color, List<List<Vector3>>>();
    foreach (var path in paths)
    {
      var line = path.Points.Select(point => new Vector3(point.x, point.z, -(point.y + LineHeight))).ToList();
      all.Add(line);
      var tint = new Color(path.Tint.r, path.Tint.g, path.Tint.b, TintAlpha);
      if (!byTint.TryGetValue(tint, out var list)) byTint[tint] = list = new List<List<Vector3>>();
      list.Add(line);
    }
    _casing.SetLines(all);
    foreach (var entry in _lines.Where(entry => !byTint.ContainsKey(entry.Key)).ToList()) entry.Value.SetLines(new List<List<Vector3>>());
    foreach (var entry in byTint)
    {
      if (!_lines.TryGetValue(entry.Key, out var line))
      {
        line = AddLine("Path");
        line.color = entry.Key;
        _lines[entry.Key] = line;
      }
      line.SetLines(entry.Value);
    }
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
    _root = new GameObject("Map Plus Paths", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    if (prefabCanvas != null) canvas.sortingLayerID = prefabCanvas.sortingLayerID;
    canvas.sortingOrder = prefabCanvas != null ? prefabCanvas.sortingOrder : 0;
    _casing = AddLine("Casing");
    _casing.color = CasingColor;
  }

  private StripLine AddLine(string name)
  {
    var lineObject = new GameObject(name, typeof(RectTransform)) { layer = Layers.Map };
    lineObject.transform.SetParent(_root.transform, false);
    var line = lineObject.AddComponent<StripLine>();
    line.raycastTarget = false;
    return line;
  }
}

internal sealed class StripLine : MaskableGraphic
{
  private readonly List<List<Vector3>> _lines = new();
  private float _width = 1f;

  internal void SetLines(List<List<Vector3>> lines)
  {
    _lines.Clear();
    _lines.AddRange(lines);
    SetVerticesDirty();
  }

  internal void Configure(float width)
  {
    if (Mathf.Abs(width - _width) < 0.001f) return;
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
        helper.AddVert(point + offset, color, Vector2.zero);
        helper.AddVert(point - offset, color, Vector2.zero);
        if (i == 0) continue;
        var vertex = start + i * 2;
        helper.AddTriangle(vertex - 2, vertex - 1, vertex);
        helper.AddTriangle(vertex - 1, vertex + 1, vertex);
      }
    }
  }

  private static Vector2 Flat(Vector3 point) => new(point.x, point.y);
}
