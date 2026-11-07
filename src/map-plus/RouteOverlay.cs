using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Messages;
using HarmonyLib;
using Helpers;
using Model;
using Model.AI;
using TMPro;
using Track;
using Track.Search;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class RouteOverlay : MonoBehaviour
{
  private const float RefreshSeconds = 0.5f;
  private const float SampleStep = 8f;
  private const float PathTolerance = 3f;
  private const int PathBudget = 400;
  private const float LineHeight = 50f;
  private const float FlagHeight = 120f;
  private const float WidthFraction = 0.02f;
  private const float FlagFraction = 0.07f;
  private const float ScrollPerSecond = 1.5f;
  private const float TrimSearch = 400f;
  private const int FlagPixels = 64;
  private const float LabelSize = 30f;

  internal static readonly Color RouteColor = new(0.55f, 0.9f, 1f);

  private static readonly FieldInfo RouteField = AccessTools.Field(typeof(AutoEngineerPlanner), "_route");
  private static readonly FieldInfo StartField = AccessTools.Field(typeof(AutoEngineerPlanner), "_startStepIndex");

  private static Sprite _flagSprite;

  private GameObject _root;
  private RouteLine _line;
  private RectTransform _flag;
  private TMP_Text _label;
  private float _nextRefresh;
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
      if (!MapPlusMod.TrainRoute.IsOn || camera == null || !camera.gameObject.activeInHierarchy)
      {
        Show(false);
        return;
      }
      if (Time.unscaledTime >= _nextRefresh)
      {
        _nextRefresh = Time.unscaledTime + RefreshSeconds;
        Refresh();
      }
      if (!_visible) return;
      _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(Vector3.zero), Quaternion.Euler(90f, 0f, 0f));
      var size = camera.orthographicSize;
      _line.Configure(size * WidthFraction, Time.unscaledTime * ScrollPerSecond);
      _flag.localScale = Vector3.one * (size * FlagFraction / FlagPixels);
    }
    catch (Exception exception)
    {
      Show(false);
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the train's route");
    }
  }

  private void Refresh()
  {
    var locomotive = TrainController.Shared?.SelectedLocomotive;
    var points = locomotive != null ? GamePoints(locomotive) : null;
    if (points == null || points.Count < 2)
    {
      Show(false);
      return;
    }
    Build();
    for (var i = 0; i < points.Count; i++) points[i] = Local(points[i], LineHeight);
    _line.SetPoints(points);
    var end = points[points.Count - 1];
    _flag.localPosition = new Vector3(end.x, end.y, end.z - (FlagHeight - LineHeight));
    _label.text = locomotive.DisplayName;
    Show(true);
  }

  private void Show(bool visible)
  {
    _visible = visible;
    if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
  }

  private static Vector3 Local(Vector3 gamePosition, float height) => new(gamePosition.x, gamePosition.z, -(gamePosition.y + height));

  internal static List<Vector3> GamePoints(BaseLocomotive locomotive)
  {
    var persistence = new AutoEngineerPersistence(locomotive.KeyValueObject);
    if (persistence.Orders.Mode != AutoEngineerMode.Waypoint || !persistence.Orders.Waypoint.HasValue) return null;
    var planner = locomotive.AutoEngineerPlanner;
    if (planner == null || RouteField?.GetValue(planner) is not List<RouteSearch.Step> route || route.Count < 2) return null;
    var start = StartField?.GetValue(planner) is int index ? Mathf.Clamp(index, 0, route.Count - 2) : 0;
    var graph = Graph.Shared;
    if (graph == null) return null;
    var points = new List<Vector3>();
    for (var i = start; i < route.Count - 1; i++) AppendLeg(graph, route[i], route[i + 1], points);
    Trim(points, locomotive.GetCenterPosition(graph));
    return points;
  }

  private static void AppendLeg(Graph graph, RouteSearch.Step from, RouteSearch.Step to, List<Vector3> points)
  {
    var distance = to.Distance;
    var target = to.Position;
    if (points.Count == 0) points.Add(from.Position);
    if (distance < 1f)
    {
      points.Add(target);
      return;
    }
    var path = PathBetween(graph, from.Location, to.Location, distance);
    if (path != null)
    {
      foreach (var piece in path)
      {
        var length = Mathf.Abs(piece.To - piece.From);
        var samples = Mathf.Max(1, Mathf.CeilToInt(length / SampleStep));
        for (var k = 1; k <= samples; k++)
        {
          points.Add(graph.GetPosition(new Location(piece.Segment, Mathf.Lerp(piece.From, piece.To, (float)k / samples), piece.End)));
        }
      }
      return;
    }
    Location? best = null;
    var bestError = float.MaxValue;
    foreach (var start in new[] { from.Location, from.Location.Flipped() })
    {
      try
      {
        var error = Vector3.Distance(graph.GetPosition(graph.LocationByMoving(start, distance, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true)), target);
        if (error >= bestError) continue;
        bestError = error;
        best = start;
      }
      catch (Exception)
      {
      }
    }
    if (!best.HasValue)
    {
      points.Add(target);
      return;
    }
    var count = Mathf.Max(1, Mathf.CeilToInt(distance / SampleStep));
    for (var k = 1; k <= count; k++)
    {
      try
      {
        points.Add(graph.GetPosition(graph.LocationByMoving(best.Value, distance * k / count, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true)));
      }
      catch (Exception)
      {
        break;
      }
    }
  }

  private static List<Piece> PathBetween(Graph graph, Location from, Location to, float distance)
  {
    if (from.segment == null || to.segment == null) return null;
    var tolerance = Mathf.Max(PathTolerance, distance * 0.1f);
    List<Piece> best = null;
    var bestError = float.MaxValue;
    foreach (var start in new[] { from, from.Flipped() })
    {
      var pieces = new List<Piece>();
      var budget = 0;
      Search(graph, start.segment, start.end, start.distance, to, distance, tolerance, 0f, pieces, ref best, ref bestError, ref budget);
    }
    return best;
  }

  private static void Search(Graph graph, TrackSegment segment, TrackSegment.End end, float enter, Location to, float distance, float tolerance, float travelled, List<Piece> pieces, ref List<Piece> best, ref float bestError, ref int budget)
  {
    if (++budget > PathBudget) return;
    var length = segment.GetLength();
    if (segment == to.segment)
    {
      var at = to.end == end ? to.distance : length - to.distance;
      if (at >= enter - 0.01f)
      {
        var error = Mathf.Abs(travelled + at - enter - distance);
        if (error <= tolerance && error < bestError)
        {
          bestError = error;
          best = new List<Piece>(pieces) { new Piece(segment, end, enter, at) };
        }
      }
    }
    var reach = travelled + length - enter;
    if (reach > distance + tolerance) return;
    var node = segment.NodeForEnd(end == TrackSegment.End.A ? TrackSegment.End.B : TrackSegment.End.A);
    if (node == null) return;
    pieces.Add(new Piece(segment, end, enter, length));
    foreach (var next in Onward(graph, node, segment))
    {
      var nextEnd = next.NodeForEnd(TrackSegment.End.A) == node ? TrackSegment.End.A : TrackSegment.End.B;
      Search(graph, next, nextEnd, 0f, to, distance, tolerance, reach, pieces, ref best, ref bestError, ref budget);
    }
    pieces.RemoveAt(pieces.Count - 1);
  }

  private static IEnumerable<TrackSegment> Onward(Graph graph, TrackNode node, TrackSegment from)
  {
    if (graph.DecodeSwitchAt(node, out var enter, out var normal, out var reversed))
    {
      if (from == enter) return new[] { normal, reversed };
      return new[] { enter };
    }
    return graph.SegmentsConnectedTo(node).Where(other => other != from);
  }

  private readonly struct Piece
  {
    public Piece(TrackSegment segment, TrackSegment.End end, float from, float to)
    {
      Segment = segment;
      End = end;
      From = from;
      To = to;
    }

    public TrackSegment Segment { get; }
    public TrackSegment.End End { get; }
    public float From { get; }
    public float To { get; }
  }

  private static void Trim(List<Vector3> points, Vector3 train)
  {
    var closest = 0;
    var closestDistance = float.MaxValue;
    var travelled = 0f;
    for (var i = 0; i < points.Count && travelled < TrimSearch; i++)
    {
      if (i > 0) travelled += Vector3.Distance(points[i - 1], points[i]);
      var distance = Vector2.Distance(new Vector2(points[i].x, points[i].z), new Vector2(train.x, train.z));
      if (distance >= closestDistance) continue;
      closestDistance = distance;
      closest = i;
    }
    if (closest > 0 && closest < points.Count - 1) points.RemoveRange(0, closest);
  }

  private void Build()
  {
    if (_root != null) return;
    _root = new GameObject("Map Plus Route", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    var order = prefabCanvas != null ? prefabCanvas.sortingOrder : 0;
    if (prefabCanvas != null) canvas.sortingLayerID = prefabCanvas.sortingLayerID;
    canvas.sortingOrder = order;

    var lineObject = new GameObject("Line", typeof(RectTransform)) { layer = Layers.Map };
    lineObject.transform.SetParent(_root.transform, false);
    _line = lineObject.AddComponent<RouteLine>();
    _line.color = RouteColor;
    _line.raycastTarget = false;

    var flagObject = new GameObject("Flag", typeof(RectTransform)) { layer = Layers.Map };
    _flag = (RectTransform)flagObject.transform;
    _flag.SetParent(_root.transform, false);
    _flag.sizeDelta = new Vector2(FlagPixels, FlagPixels);
    _flag.pivot = new Vector2(8f / FlagPixels, 0f);
    var flagCanvas = flagObject.AddComponent<Canvas>();
    flagCanvas.overrideSorting = true;
    if (prefabCanvas != null) flagCanvas.sortingLayerID = prefabCanvas.sortingLayerID;
    flagCanvas.sortingOrder = order + CarIcons.LocomotiveOrderOffset + 1;
    var image = flagObject.AddComponent<Image>();
    image.sprite = FlagSprite;
    image.color = RouteColor;
    image.raycastTarget = false;

    var labelObject = new GameObject("Label", typeof(RectTransform)) { layer = Layers.Map };
    var labelRect = (RectTransform)labelObject.transform;
    labelRect.SetParent(_flag, false);
    labelRect.anchorMin = labelRect.anchorMax = new Vector2(1f, 1f);
    labelRect.pivot = new Vector2(0f, 1f);
    labelRect.anchoredPosition = new Vector2(4f, 0f);
    labelRect.sizeDelta = new Vector2(400f, 40f);
    var label = labelObject.AddComponent<TextMeshProUGUI>();
    var prefabText = prefab != null ? prefab.GetComponentInChildren<TMP_Text>(true) : null;
    if (prefabText != null)
    {
      label.font = prefabText.font;
      label.fontSharedMaterial = prefabText.fontSharedMaterial;
    }
    label.fontSize = LabelSize;
    label.fontStyle = FontStyles.Bold;
    label.color = RouteColor;
    label.alignment = TextAlignmentOptions.TopLeft;
    label.textWrappingMode = TextWrappingModes.NoWrap;
    label.raycastTarget = false;
    _label = label;
  }

  private static Sprite FlagSprite => _flagSprite != null ? _flagSprite : _flagSprite = CreateFlag();

  private static Sprite CreateFlag()
  {
    var size = FlagPixels;
    var pixels = new Color32[size * size];
    for (var y = 0; y < size; y++)
    {
      for (var x = 0; x < size; x++)
      {
        var pole = x >= 6 && x <= 10;
        var top = size - 2;
        var pennant = x > 10 && y >= top - 28 && y <= top && x - 10 <= (14 - Mathf.Abs(y - (top - 14))) * 3.2f;
        pixels[y * size + x] = pole || pennant ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
      }
    }
    return Sprites.Create(size, pixels);
  }
}

internal sealed class RouteLine : MaskableGraphic
{
  private const int StripeLength = 64;
  private const int StripeHeight = 16;
  private const float PeriodPerWidth = 2.5f;
  private const float MiterLimit = 3f;

  private static Texture2D _stripes;

  private readonly List<Vector3> _points = new();
  private float _width = 1f;
  private float _offset;

  public override Texture mainTexture => Stripes;

  internal void SetPoints(List<Vector3> points)
  {
    _points.Clear();
    _points.AddRange(points);
    SetVerticesDirty();
  }

  internal void Configure(float width, float offset)
  {
    _width = Mathf.Max(0.01f, width);
    _offset = offset;
    SetVerticesDirty();
  }

  protected override void OnPopulateMesh(VertexHelper helper)
  {
    helper.Clear();
    if (_points.Count < 2) return;
    var half = _width / 2f;
    var period = _width * PeriodPerWidth;
    var along = 0f;
    var lastNormal = Vector2.up;
    for (var i = 0; i < _points.Count; i++)
    {
      var point = _points[i];
      var previous = i > 0 ? Flat(point) - Flat(_points[i - 1]) : Vector2.zero;
      var next = i < _points.Count - 1 ? Flat(_points[i + 1]) - Flat(point) : Vector2.zero;
      var tangent = previous.normalized + next.normalized;
      Vector2 normal;
      var scale = 1f;
      if (tangent.sqrMagnitude < 0.0001f)
      {
        normal = lastNormal;
      }
      else
      {
        tangent.Normalize();
        normal = new Vector2(-tangent.y, tangent.x);
        var segment = next.sqrMagnitude > 0f ? next.normalized : previous.normalized;
        var segmentNormal = new Vector2(-segment.y, segment.x);
        scale = Mathf.Min(MiterLimit, 1f / Mathf.Max(0.0001f, Mathf.Abs(Vector2.Dot(normal, segmentNormal))));
      }
      lastNormal = normal;
      if (i > 0) along += previous.magnitude;
      var u = along / period - _offset;
      var offset = new Vector3(normal.x, normal.y, 0f) * (half * scale);
      helper.AddVert(point + offset, color, new Vector2(u, 1f));
      helper.AddVert(point - offset, color, new Vector2(u, 0f));
      if (i == 0) continue;
      var vertex = i * 2;
      helper.AddTriangle(vertex - 2, vertex - 1, vertex);
      helper.AddTriangle(vertex - 1, vertex + 1, vertex);
    }
  }

  private static Vector2 Flat(Vector3 point) => new(point.x, point.y);

  private static Texture2D Stripes => _stripes != null ? _stripes : _stripes = CreateStripes();

  private static Texture2D CreateStripes()
  {
    var texture = new Texture2D(StripeLength, StripeHeight, TextureFormat.RGBA32, false)
    {
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSave
    };
    var pixels = new Color32[StripeLength * StripeHeight];
    for (var y = 0; y < StripeHeight; y++)
    {
      for (var x = 0; x < StripeLength; x++)
      {
        Color32 pixel;
        if (y < 3 || y >= StripeHeight - 3) pixel = new Color32(0, 0, 0, 220);
        else if ((x + y * 2) % StripeLength < StripeLength / 2) pixel = new Color32(255, 255, 255, 255);
        else pixel = new Color32(40, 60, 70, 235);
        pixels[y * StripeLength + x] = pixel;
      }
    }
    texture.SetPixels32(pixels);
    texture.Apply();
    return texture;
  }
}
