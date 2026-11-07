using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Track;
using Track.Signals;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class SignalIcons : MonoBehaviour
{
  private const float ListSeconds = 3f;
  private const float AspectSeconds = 0.25f;
  private const float Height = 80f;
  private const float SizeFraction = 0.075f;
  private const float SideFraction = 0.7f;
  private const int SpritePixels = 64;

  private static readonly Color StopColor = new(1f, 0.25f, 0.22f);
  private static readonly Color ApproachColor = new(1f, 0.84f, 0.2f);
  private static readonly Color ClearColor = new(0.3f, 0.9f, 0.4f);
  private static readonly Color RestrictingColor = new(1f, 0.55f, 0.15f);

  private static Sprite _arrow;
  private static readonly List<Marker> Markers = new();

  private GameObject _root;
  private float _nextList;
  private float _nextAspect;
  private float _lastSize = -1f;
  private bool _logged;

  internal static (CTCSignal Signal, SignalAspect Aspect)? SignalAt(Vector3 gamePoint, float radius)
  {
    Marker closest = null;
    var closestDistance = float.MaxValue;
    foreach (var marker in Markers)
    {
      if (marker.Signal == null) continue;
      var distance = Vector2.Distance(marker.Drawn, new Vector2(gamePoint.x, gamePoint.z));
      if (distance >= Mathf.Max(radius, marker.Reach) || distance >= closestDistance) continue;
      closestDistance = distance;
      closest = marker;
    }
    return closest == null ? null : (closest.Signal, closest.Signal.CurrentAspect);
  }

  internal static string NameFor(string signalId)
  {
    var signal = Markers.Select(marker => marker.Signal).FirstOrDefault(candidate => candidate != null && candidate.id == signalId)
      ?? (Markers.Count == 0 ? FindObjectsByType<CTCSignal>(FindObjectsSortMode.None).FirstOrDefault(candidate => candidate != null && candidate.id == signalId) : null);
    return signal == null || signal.IsIntermediate ? null : signal.DisplayName;
  }

  internal static string AspectName(SignalAspect aspect) => aspect switch
  {
    SignalAspect.Stop => "Stop",
    SignalAspect.Approach => "Approach",
    SignalAspect.Clear => "Clear",
    SignalAspect.DivergingApproach => "Diverging approach",
    SignalAspect.DivergingClear => "Diverging clear",
    SignalAspect.Restricting => "Restricting",
    _ => aspect.ToString()
  };

  internal static Color ColorFor(SignalAspect aspect) => aspect switch
  {
    SignalAspect.Stop => StopColor,
    SignalAspect.Approach or SignalAspect.DivergingApproach => ApproachColor,
    SignalAspect.Clear or SignalAspect.DivergingClear => ClearColor,
    _ => RestrictingColor
  };

  private void OnDestroy()
  {
    Markers.Clear();
    if (_root != null) Destroy(_root);
  }

  private void Update()
  {
    try
    {
      var builder = MapBuilder.Shared;
      var camera = builder != null ? builder.mapCamera : null;
      var shown = MapPlusMod.Signals.IsOn && camera != null && camera.gameObject.activeInHierarchy;
      if (_root != null && _root.activeSelf != shown) _root.SetActive(shown);
      if (!shown) return;
      if (Time.unscaledTime >= _nextList)
      {
        _nextList = Time.unscaledTime + ListSeconds;
        RefreshList();
        _lastSize = -1f;
      }
      if (_root == null) return;
      _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(Vector3.zero), Quaternion.Euler(90f, 0f, 0f));
      var size = camera.orthographicSize;
      if (!Mathf.Approximately(size, _lastSize))
      {
        _lastSize = size;
        Place(size);
      }
      if (Time.unscaledTime >= _nextAspect)
      {
        _nextAspect = Time.unscaledTime + AspectSeconds;
        foreach (var marker in Markers)
        {
          if (marker.Signal != null) marker.Image.color = ColorFor(marker.Signal.CurrentAspect);
        }
      }
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the signals");
    }
  }

  private void RefreshList()
  {
    var signals = FindObjectsByType<CTCSignal>(FindObjectsSortMode.None).Where(signal => signal != null && signal.isActiveAndEnabled).ToList();
    if (signals.Count == Markers.Count && signals.All(signal => Markers.Any(marker => marker.Signal == signal))) return;
    Build();
    foreach (var marker in Markers) Destroy(marker.Image.gameObject);
    Markers.Clear();
    var graph = Graph.Shared;
    if (graph == null) return;
    foreach (var signal in signals)
    {
      if (!TryPose(graph, signal, out var position, out var direction)) continue;
      var child = new GameObject("Signal", typeof(RectTransform)) { layer = Layers.Map };
      child.transform.SetParent(_root.transform, false);
      var image = child.AddComponent<Image>();
      image.sprite = Arrow;
      image.raycastTarget = false;
      image.color = ColorFor(signal.CurrentAspect);
      Markers.Add(new Marker(signal, position, direction, image));
    }
  }

  internal static bool TryPose(Graph graph, CTCSignal signal, out Vector3 position, out Vector2 direction)
  {
    var marker = signal.GetComponent<TrackMarker>() ?? signal.GetComponentInParent<TrackMarker>();
    var location = marker != null ? marker.Location : null;
    if (location.HasValue && location.Value.IsValid)
    {
      var pose = graph.GetPositionDirection(location.Value);
      position = pose.Position;
      direction = new Vector2(pose.Direction.x, pose.Direction.z).normalized;
      return true;
    }
    position = WorldTransformer.WorldToGame(signal.transform.position);
    direction = Vector2.zero;
    return true;
  }

  private void Place(float orthographicSize)
  {
    var size = orthographicSize * SizeFraction;
    foreach (var marker in Markers)
    {
      var rect = marker.Image.rectTransform;
      var right = new Vector2(marker.Direction.y, -marker.Direction.x);
      var offset = right * (size * SideFraction);
      rect.sizeDelta = new Vector2(size, size);
      rect.localPosition = new Vector3(marker.Position.x + offset.x, marker.Position.z + offset.y, -(marker.Position.y + Height));
      marker.Drawn = new Vector2(marker.Position.x + offset.x, marker.Position.z + offset.y);
      marker.Reach = size * 0.5f;
      var angle = marker.Direction == Vector2.zero ? 0f : Mathf.Atan2(marker.Direction.y, marker.Direction.x) * Mathf.Rad2Deg;
      rect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
  }

  private void Build()
  {
    if (_root != null) return;
    _root = new GameObject("Map Plus Signals", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    if (prefabCanvas != null)
    {
      canvas.sortingLayerID = prefabCanvas.sortingLayerID;
      canvas.sortingOrder = prefabCanvas.sortingOrder;
    }
  }

  private static Sprite Arrow => _arrow != null ? _arrow : _arrow = CreateArrow();

  private static Sprite CreateArrow()
  {
    var size = SpritePixels;
    var pixels = new Color32[size * size];
    var tip = new Vector2(size - 6f, size / 2f);
    var top = new Vector2(10f, size - 10f);
    var bottom = new Vector2(10f, 10f);
    for (var y = 0; y < size; y++)
    {
      for (var x = 0; x < size; x++)
      {
        var point = new Vector2(x + 0.5f, y + 0.5f);
        var inside = Inside(point, tip, top, bottom, 0f);
        var core = Inside(point, tip, top, bottom, 5f);
        pixels[y * size + x] = core ? new Color32(255, 255, 255, 255) : inside ? new Color32(20, 20, 20, 230) : new Color32(0, 0, 0, 0);
      }
    }
    return Sprites.Create(size, pixels);
  }

  private static bool Inside(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float inset)
  {
    return Edge(point, a, b) >= inset && Edge(point, b, c) >= inset && Edge(point, c, a) >= inset;
  }

  private static float Edge(Vector2 point, Vector2 from, Vector2 to)
  {
    var edge = to - from;
    var normal = new Vector2(edge.y, -edge.x).normalized;
    return -Vector2.Dot(point - from, normal);
  }

  private sealed class Marker
  {
    public Marker(CTCSignal signal, Vector3 position, Vector2 direction, Image image)
    {
      Signal = signal;
      Position = position;
      Direction = direction;
      Image = image;
      Drawn = new Vector2(position.x, position.z);
    }

    public CTCSignal Signal { get; }
    public Vector3 Position { get; }
    public Vector2 Direction { get; }
    public Image Image { get; }
    public Vector2 Drawn { get; set; }
    public float Reach { get; set; }
  }
}
