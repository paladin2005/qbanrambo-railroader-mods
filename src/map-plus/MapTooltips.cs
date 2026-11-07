using System;
using System.Collections.Generic;
using Helpers;
using Model;
using Track;
using UI.Map;
using UI.Tooltips;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal sealed class MapTooltips : MonoBehaviour
{
  private const float InstallSeconds = 1f;
  private const float CacheSeconds = 0.5f;
  private const float PickRadius = 8f;
  private const float TrackSeconds = 0.1f;

  private MapDrag _drag;
  private UITooltipProvider _provider;
  private Func<TooltipInfo> _previous;
  private bool _addedProvider;
  private float _nextInstall;
  private object _lastTarget;
  private float _lastTime;
  private TooltipInfo _last;
  private bool _logged;
  private Vector3 _trackMouse;
  private float _trackTime = float.MinValue;
  private bool _trackFound;
  private Location _track;

  private void Update()
  {
    if (_provider != null || Time.unscaledTime < _nextInstall) return;
    _nextInstall = Time.unscaledTime + InstallSeconds;
    try
    {
      Install();
    }
    catch (Exception exception)
    {
      LogOnce(exception, "Couldn't add tooltips to the map");
    }
  }

  private void OnDestroy()
  {
    if (_provider == null) return;
    if (_addedProvider) Destroy(_provider);
    else _provider.DynamicTooltipInfo = _previous;
  }

  private void Install()
  {
    _drag = FindAnyObjectByType<MapDrag>(FindObjectsInactive.Include);
    if (_drag == null) return;
    _provider = _drag.GetComponent<UITooltipProvider>();
    _addedProvider = _provider == null;
    if (_addedProvider) _provider = _drag.gameObject.AddComponent<UITooltipProvider>();
    _previous = _provider.DynamicTooltipInfo;
    _provider.DynamicTooltipInfo = Hover;
  }

  private TooltipInfo Hover()
  {
    try
    {
      if (!MapMouse.TryGetRay(_drag, out var ray, out var metersPerUnit)) return Previous();
      var car = CarAt(ray);
      if (car != null) return Cached(car, () => CarInfo.Tooltip(car));
      var radius = PickRadius * metersPerUnit;
      var point = WorldTransformer.WorldToGame(ray.origin);
      if (OrderMarkers.At(point, out var orderKey) is { } orders) return Cached(orderKey, () => orders);
      if (PlaceIcons.At(point) is { } poi) return Cached(poi, () => PlaceIcons.Tooltip(poi));
      if (MapPlusMod.Fusees.IsOn && FuseeIcons.At(point, radius) is { } fusee)
      {
        return Cached(fusee.Key, () => new TooltipInfo("Fusee", "The engineer stops here.\n<alpha=#99>Right-click the track to put it out"));
      }
      if (MapPlusMod.Signals.IsOn && SignalIcons.SignalAt(point, radius) is { } found)
      {
        return Cached(found.Signal, () => SignalTooltip(found.Signal, found.Aspect));
      }
      var place = TrackPainter.PlaceAt(point, radius);
      if (TrackUnderMouse(ray, radius, out var location) && location.segment != null)
      {
        return Cached(location.segment, () =>
        {
          var lines = place != null ? CarInfo.PlaceLines(place) : new List<string>();
          if (place == null && TrackInfo.Town(location) is { } town) lines.Add(town);
          lines.AddRange(TrackInfo.Describe(location.segment, location));
          return new TooltipInfo(place?.Name ?? TrackInfo.KindName(location.segment), string.Join("\n", lines));
        });
      }
      if (place != null) return Cached(place, () => CarInfo.PlaceTooltip(place));
      return Previous();
    }
    catch (Exception exception)
    {
      LogOnce(exception, "Couldn't build a map tooltip");
      return TooltipInfo.Empty;
    }
  }

  private static TooltipInfo SignalTooltip(Track.Signals.CTCSignal signal, Track.Signals.SignalAspect aspect)
  {
    var color = ColorUtility.ToHtmlStringRGB(SignalIcons.ColorFor(aspect));
    var title = signal.IsIntermediate ? "Automatic signal" : $"{signal.DisplayName} signal";
    var kind = signal.IsIntermediate ? "Block signal: at Stop, trains stop and go on slowly" : "Interlocking: at Stop, trains wait";
    return new TooltipInfo(title, $"<color=#{color}>{SignalIcons.AspectName(aspect)}</color>\n{kind}");
  }

  private bool TrackUnderMouse(Ray ray, float radius, out Location location)
  {
    var mouse = Input.mousePosition;
    if ((mouse - _trackMouse).sqrMagnitude > 1f || Time.unscaledTime - _trackTime > TrackSeconds)
    {
      _trackMouse = mouse;
      _trackTime = Time.unscaledTime;
      _trackFound = MapMouse.TryGetTrack(ray, radius, out _track);
    }
    location = _track;
    return _trackFound;
  }

  private TooltipInfo Previous() => _previous?.Invoke() ?? TooltipInfo.Empty;

  private TooltipInfo Cached(object target, Func<TooltipInfo> build)
  {
    if (ReferenceEquals(target, _lastTarget) && Time.unscaledTime - _lastTime < CacheSeconds) return _last;
    _last = build();
    _lastTarget = target;
    _lastTime = Time.unscaledTime;
    return _last;
  }

  private static Car CarAt(Ray ray)
  {
    var camera = MapBuilder.Shared.mapCamera;
    if (!Physics.Raycast(ray, out var hit, camera.transform.position.y, 1 << Layers.Map)) return null;
    return hit.collider.GetComponentInParent<Car>();
  }

  private void LogOnce(Exception exception, string message)
  {
    if (_logged) return;
    _logged = true;
    MapPlusMod.Log?.Error(exception, message);
  }
}
