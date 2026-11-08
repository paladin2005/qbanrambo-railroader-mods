using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Helpers;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Ops;
using TMPro;
using Track;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class OrderMarkers : MonoBehaviour
{
  private const float RefreshSeconds = 0.5f;
  private const float Height = 130f;
  private const float MarkerUnits = 20f;
  private const float CurrentGrowth = 1.25f;
  private const float MergeMeters = 8f;
  private const float LabelFont = 32f;

  private static readonly Color MarkerColor = new(1f, 0.75f, 0.38f);
  private static readonly Color CurrentColor = new(1f, 0.88f, 0.55f);
  private static readonly Color MarkerRing = new(0f, 0f, 0f, 0.35f);
  private static readonly Color LabelColor = new(0.1f, 0.08f, 0.05f);
  private static readonly List<Marker> Markers = new();

  private static string _locomotiveName;

  private GameObject _root;
  private TMP_Text _fontSource;
  private string _signature;
  private float _nextRefresh;
  private float _lastSize = -1f;
  private float _lastUnit = -1f;
  private bool _visible;
  private bool _logged;

  internal static TooltipInfo? At(Vector3 gamePoint, out object key)
  {
    key = null;
    if (!MapPlusMod.TrainOrders.IsOn) return null;
    foreach (var marker in Markers)
    {
      if (marker.Rect == null || !marker.Rect.gameObject.activeInHierarchy) continue;
      var distance = Vector2.Distance(new Vector2(marker.Position.x, marker.Position.z), new Vector2(gamePoint.x, gamePoint.z));
      if (distance > marker.Radius) continue;
      key = marker;
      var lines = marker.Steps.Select(step => step.Current ? $"<color=#FFC060>{step.Number}. {step.Text}</color>" : $"{step.Number}. {step.Text}");
      return new TooltipInfo($"{_locomotiveName}'s orders", string.Join("\n", lines));
    }
    return null;
  }

  private void OnDestroy()
  {
    Markers.Clear();
    if (_root != null) Destroy(_root);
  }

  private void Update()
  {
    try
    {
      var mapShown = MapMouse.IsMapShown(out var camera);
      if (!mapShown || !MapPlusMod.TrainOrders.IsOn)
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
      var unit = MapMouse.MetersPerUnit(camera);
      if (Mathf.Approximately(size, _lastSize) && Mathf.Approximately(unit, _lastUnit)) return;
      _lastSize = size;
      _lastUnit = unit;
      Place(unit);
    }
    catch (Exception exception)
    {
      Show(false);
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the train's orders");
    }
  }

  private void Show(bool visible)
  {
    _visible = visible;
    if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
  }

  private void Refresh()
  {
    var locomotive = TrainController.Shared?.SelectedLocomotive;
    var steps = locomotive != null ? Steps(locomotive) : new List<Step>();
    var groups = new List<(Vector3 Position, List<Step> Steps)>();
    foreach (var step in steps)
    {
      var index = groups.FindIndex(group => Vector3.Distance(group.Position, step.Position) < MergeMeters);
      if (index >= 0) groups[index].Steps.Add(step);
      else groups.Add((step.Position, new List<Step> { step }));
    }
    var signature = string.Join("|", groups.Select(group => $"{Mathf.RoundToInt(group.Position.x)},{Mathf.RoundToInt(group.Position.z)}:{string.Join(",", group.Steps.Select(step => $"{step.Number}{(step.Current ? "*" : "")}{step.Text}"))}"));
    _locomotiveName = locomotive != null ? locomotive.DisplayName : null;
    if (groups.Count == 0)
    {
      _signature = signature;
      Show(false);
      return;
    }
    if (signature != _signature)
    {
      _signature = signature;
      Rebuild(groups);
      _lastSize = -1f;
    }
    Show(true);
  }

  private static List<Step> Steps(BaseLocomotive locomotive)
  {
    var result = new List<Step>();
    Value value;
    try
    {
      value = locomotive.KeyValueObject[WebTrain.OrdersKey];
    }
    catch (Exception)
    {
      return result;
    }
    if (value.IsNull) return result;
    var stepValues = value["steps"];
    if (stepValues.IsNull) return result;
    var steps = stepValues.ArrayValue.ToList();
    var at = value["at"].IntValueOrDefault(0);
    var repeat = value["repeat"].BoolValueOrDefault(false);
    var running = value["run"].BoolValueOrDefault(false);
    var graph = Graph.Shared;
    if (graph == null) return result;
    for (var index = 0; index < steps.Count; index++)
    {
      if (index < at && !repeat) continue;
      var position = PositionOf(steps[index], graph);
      if (!position.HasValue) continue;
      result.Add(new Step(index + 1, WebTrain.Describe(steps[index]), position.Value, running && index == at));
    }
    return result;
  }

  internal static Vector3? PositionOf(Value step, Graph graph)
  {
    try
    {
      switch (step["kind"].IntValueOrDefault(0))
      {
        case 0:
          var waypoint = OrderWaypoint.FromPropertyValue(step["wpt"]);
          if (!waypoint.HasValue) return null;
          if (waypoint.Value.WantsCouple && TrainController.Shared.TryGetCarForId(waypoint.Value.CoupleToCarId, out var car) && car != null)
          {
            var body = car.LastBodyPosition;
            if (body != null && body.Length >= 2) return Vector3.Lerp(body[0], body[1], 0.5f);
          }
          if (string.IsNullOrEmpty(waypoint.Value.LocationString)) return null;
          return graph.GetPosition(graph.ResolveLocationString(waypoint.Value.LocationString));
        case 6:
          var id = step["station"].StringValueOrDefault(null);
          var stop = OpsController.Shared?.AllPassengerStops?.FirstOrDefault(candidate => candidate != null && candidate.identifier == id);
          return stop != null ? stop.CenterPoint : null;
        case 7:
          var first = (step["fuel"].StringValueOrDefault("") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
          var fuel = first != null ? PlaceData.FuelStop(first) : null;
          return fuel?.Point;
        case 8:
          var turntable = WebTrain.FindTurntable(step["turntable"].StringValueOrDefault(null));
          return turntable != null ? turntable.Position : null;
        default:
          return null;
      }
    }
    catch (Exception)
    {
      return null;
    }
  }

  private void Rebuild(List<(Vector3 Position, List<Step> Steps)> groups)
  {
    Build();
    foreach (var marker in Markers)
    {
      if (marker.Rect != null) Destroy(marker.Rect.gameObject);
    }
    Markers.Clear();
    foreach (var group in groups)
    {
      var current = group.Steps.Any(step => step.Current);
      var markerObject = new GameObject("Order", typeof(RectTransform)) { layer = Layers.Map };
      var rect = (RectTransform)markerObject.transform;
      rect.SetParent(_root.transform, false);
      rect.sizeDelta = new Vector2(Glyphs.Size, Glyphs.Size);
      var image = markerObject.AddComponent<Image>();
      image.sprite = Glyphs.Disc(current ? CurrentColor : MarkerColor, current ? Color.white : MarkerRing);
      image.raycastTarget = false;
      var labelObject = new GameObject("Number", typeof(RectTransform)) { layer = Layers.Map };
      var labelRect = (RectTransform)labelObject.transform;
      labelRect.SetParent(rect, false);
      labelRect.anchorMin = Vector2.zero;
      labelRect.anchorMax = Vector2.one;
      labelRect.offsetMin = Vector2.zero;
      labelRect.offsetMax = Vector2.zero;
      var label = labelObject.AddComponent<TextMeshProUGUI>();
      if (_fontSource != null)
      {
        label.font = _fontSource.font;
        label.fontSharedMaterial = _fontSource.fontSharedMaterial;
      }
      var first = group.Steps.Min(step => step.Number);
      label.text = group.Steps.Count > 1 ? $"{first}+" : first.ToString();
      label.fontSize = group.Steps.Count > 1 || first >= 10 ? LabelFont * 0.8f : LabelFont;
      label.fontStyle = FontStyles.Bold;
      label.color = LabelColor;
      label.alignment = TextAlignmentOptions.Center;
      label.textWrappingMode = TextWrappingModes.NoWrap;
      label.overflowMode = TextOverflowModes.Overflow;
      label.raycastTarget = false;
      Markers.Add(new Marker(group.Position, group.Steps.OrderBy(step => step.Number).ToList(), current, rect));
    }
  }

  private void Place(float unit)
  {
    foreach (var marker in Markers)
    {
      var meters = MarkerUnits * unit * (marker.Current ? CurrentGrowth : 1f);
      marker.Radius = meters / 2f;
      marker.Rect.localScale = Vector3.one * (meters / Glyphs.Size);
      marker.Rect.localPosition = new Vector3(marker.Position.x, marker.Position.z, -(marker.Position.y + Height));
    }
  }

  private void Build()
  {
    if (_root != null) return;
    _root = new GameObject("Map Plus Orders", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    var order = prefabCanvas != null ? prefabCanvas.sortingOrder : 0;
    if (prefabCanvas != null) canvas.sortingLayerID = prefabCanvas.sortingLayerID;
    canvas.sortingOrder = order + CarIcons.LocomotiveOrderOffset + 1;
    _fontSource = prefab != null ? prefab.GetComponentInChildren<TMP_Text>(true) : null;
  }

  private readonly struct Step
  {
    public Step(int number, string text, Vector3 position, bool current)
    {
      Number = number;
      Text = text;
      Position = position;
      Current = current;
    }

    public int Number { get; }
    public string Text { get; }
    public Vector3 Position { get; }
    public bool Current { get; }
  }

  private sealed class Marker
  {
    public Marker(Vector3 position, List<Step> steps, bool current, RectTransform rect)
    {
      Position = position;
      Steps = steps;
      Current = current;
      Rect = rect;
    }

    public Vector3 Position { get; }
    public List<Step> Steps { get; }
    public bool Current { get; }
    public RectTransform Rect { get; }
    public float Radius { get; set; }
  }
}
