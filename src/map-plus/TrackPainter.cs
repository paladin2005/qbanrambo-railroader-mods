using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.Ops;
using Track;
using Track.Signals;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal sealed class TrackPainter : MonoBehaviour
{
  private const float RefreshSeconds = 5f;
  private const float SampleStep = 10f;
  private const float DestinationSeconds = 1f;

  private static readonly Color IndustryColor = new(0.3f, 0.65f, 1f);
  private static readonly Color MainlineColor = new(0.3f, 0.75f, 0.3f);
  private static readonly Color ComingColor = new(1f, 0.72f, 0.15f);
  private static readonly Color SpottedColor = new(0.48f, 0.9f, 0.48f);
  private static readonly FieldInfo NeedsRefreshField = typeof(MapBuilder).GetField("_segmentColorsNeedRefresh", BindingFlags.Instance | BindingFlags.NonPublic);

  private static HashSet<TrackSegment> _industry = new();
  private static HashSet<TrackSegment> _mainline = new();
  private static HashSet<TrackSegment> _coming = new();
  private static HashSet<TrackSegment> _spotted = new();
  private static List<(Place Place, Vector3[] Points)> _lines = new();

  private static int _classSignature;

  private float _nextRefresh;
  private float _nextDestinations;
  private bool _logged;

  internal static bool IsIndustry(TrackSegment segment) => _industry.Contains(segment);

  internal static bool IsMainline(TrackSegment segment) => _mainline.Contains(segment);

  internal static Color? ColorFor(TrackSegment segment)
  {
    if (!TrackInfo.IsBuilt(segment)) return null;
    if (MapPlusMod.TrainDestinations.IsOn)
    {
      if (_coming.Contains(segment)) return ComingColor;
      if (_spotted.Contains(segment)) return SpottedColor;
    }
    if (MapPlusMod.TrackClass.IsOn) return TrackInfo.ColorFor(segment);
    if (MapPlusMod.IndustryTracks.IsOn && _industry.Contains(segment)) return IndustryColor;
    if (MapPlusMod.Mainline.IsOn && _mainline.Contains(segment)) return MainlineColor;
    return null;
  }

  internal static Place PlaceAt(Vector3 gamePoint, float radius)
  {
    Place closest = null;
    var closestDistance = radius;
    foreach (var (place, points) in _lines)
    {
      for (var i = 0; i < points.Length - 1; i++)
      {
        var distance = DistanceXZ(gamePoint, points[i], points[i + 1]);
        if (distance >= closestDistance) continue;
        closestDistance = distance;
        closest = place;
      }
    }
    return closest;
  }

  private void OnEnable()
  {
    if (!MapPlusMod.MapEnabled) return;
    MapPlusMod.IndustryTracks.Changed += OnOptionChanged;
    MapPlusMod.Mainline.Changed += OnOptionChanged;
    MapPlusMod.TrackClass.Changed += OnOptionChanged;
    MapPlusMod.TrainDestinations.Changed += OnOptionChanged;
    MapPlusMod.TrackClass.Changed += OnTrackClassChanged;
    MapDisplayOption.TrackCondition.Changed += OnTrackConditionChanged;
    if (MapPlusMod.TrackClass.IsOn) MapDisplayOption.TrackCondition.IsOn = false;
    if (NeedsRefreshField == null) MapPlusMod.Log?.Error("MapBuilder._segmentColorsNeedRefresh not found; track colors won't change until the map is rebuilt");
  }

  private void OnDisable()
  {
    if (!MapPlusMod.MapEnabled) return;
    MapPlusMod.IndustryTracks.Changed -= OnOptionChanged;
    MapPlusMod.Mainline.Changed -= OnOptionChanged;
    MapPlusMod.TrackClass.Changed -= OnOptionChanged;
    MapPlusMod.TrainDestinations.Changed -= OnOptionChanged;
    MapPlusMod.TrackClass.Changed -= OnTrackClassChanged;
    MapDisplayOption.TrackCondition.Changed -= OnTrackConditionChanged;
  }

  private static void OnTrackClassChanged(bool on)
  {
    if (on) MapDisplayOption.TrackCondition.IsOn = false;
  }

  private static void OnTrackConditionChanged(bool on)
  {
    if (on) MapPlusMod.TrackClass.IsOn = false;
  }

  private void OnDestroy()
  {
    _industry = new HashSet<TrackSegment>();
    _mainline = new HashSet<TrackSegment>();
    _coming = new HashSet<TrackSegment>();
    _spotted = new HashSet<TrackSegment>();
    _lines = new List<(Place, Vector3[])>();
    _classSignature = 0;
    if (MapPlusMod.MapEnabled) Repaint();
  }

  private void OnOptionChanged(bool _)
  {
    _classSignature = MapPlusMod.TrackClass.IsOn ? TrackInfo.Signature() : 0;
    Repaint();
  }

  private void Update()
  {
    if (MapPlusMod.MapEnabled && Time.unscaledTime >= _nextDestinations)
    {
      _nextDestinations = Time.unscaledTime + DestinationSeconds;
      try
      {
        RefreshDestinations();
      }
      catch (Exception exception)
      {
        if (!_logged)
        {
          _logged = true;
          MapPlusMod.Log?.Error(exception, "Couldn't find the train's destinations");
        }
      }
    }
    if (Time.unscaledTime < _nextRefresh) return;
    _nextRefresh = Time.unscaledTime + RefreshSeconds;
    try
    {
      Refresh();
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't find the industry tracks and mainline");
    }
  }

  private static void Refresh()
  {
    var industry = new HashSet<TrackSegment>();
    var lines = new List<(Place, Vector3[])>();
    foreach (var town in Places.Towns())
    {
      foreach (var place in town.Places)
      {
        var placeSegments = new HashSet<TrackSegment>();
        foreach (var span in place.Spans) placeSegments.UnionWith(span.GetSegments());
        foreach (var segment in placeSegments)
        {
          if (segment == null) continue;
          industry.Add(segment);
          lines.Add((place, Sample(segment)));
        }
      }
    }
    _lines = lines;
    var mainline = Mainline();
    var classSignature = MapPlusMod.TrackClass.IsOn ? TrackInfo.Signature() : 0;
    var classesChanged = classSignature != _classSignature;
    _classSignature = classSignature;
    if (industry.SetEquals(_industry) && mainline.SetEquals(_mainline) && !classesChanged) return;
    _industry = industry;
    _mainline = mainline;
    if (MapPlusMod.MapEnabled) Repaint();
  }

  private static void RefreshDestinations()
  {
    var coming = new HashSet<TrackSegment>();
    var spotted = new HashSet<TrackSegment>();
    var locomotive = TrainController.Shared?.SelectedLocomotive;
    var ops = OpsController.Shared;
    if (locomotive != null && ops != null && MapPlusMod.TrainDestinations.IsOn)
    {
      foreach (var car in Consist.Of(locomotive))
      {
        if (car == null || car is BaseLocomotive || !ops.TryGetDestinationInfo(car, out _, out var arrived, out _, out var position)) continue;
        var target = arrived ? spotted : coming;
        foreach (var span in position.Spans ?? Enumerable.Empty<TrackSpan>())
        {
          if (span == null) continue;
          foreach (var segment in span.GetSegments())
          {
            if (segment != null) target.Add(segment);
          }
        }
      }
    }
    spotted.ExceptWith(coming);
    if (coming.SetEquals(_coming) && spotted.SetEquals(_spotted)) return;
    _coming = coming;
    _spotted = spotted;
    Repaint();
  }

  private static HashSet<TrackSegment> Mainline()
  {
    var segments = new HashSet<TrackSegment>();
    var blocks = CTCPanelController.Shared?.AllBlocks;
    if (blocks == null) return segments;
    foreach (var block in blocks.Values)
    {
      if (block == null) continue;
      foreach (var span in block.GetComponentsInChildren<TrackSpan>())
      {
        foreach (var segment in span.GetSegments())
        {
          if (segment != null) segments.Add(segment);
        }
      }
    }
    return segments;
  }

  private static Vector3[] Sample(TrackSegment segment)
  {
    var length = segment.GetLength();
    var steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleStep));
    var points = new Vector3[steps + 1];
    for (var i = 0; i <= steps; i++)
    {
      segment.GetPositionRotationAtDistance(length * i / steps, TrackSegment.End.A, PositionAccuracy.Standard, out points[i], out _);
    }
    return points;
  }

  private static float DistanceXZ(Vector3 point, Vector3 a, Vector3 b)
  {
    var p = new Vector2(point.x, point.z);
    var start = new Vector2(a.x, a.z);
    var along = new Vector2(b.x - a.x, b.z - a.z);
    var lengthSquared = along.sqrMagnitude;
    var t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(p - start, along) / lengthSquared) : 0f;
    return Vector2.Distance(p, start + along * t);
  }

  private static void Repaint()
  {
    var builder = MapBuilder.Shared;
    if (builder != null && NeedsRefreshField != null) NeedsRefreshField.SetValue(builder, true);
  }
}

[HarmonyPatch(typeof(MapBuilder), "ColorForSegment")]
internal static class TrackColorPatch
{
  private static bool _logged;

  private static void Postfix(TrackSegment segment, ref Color __result)
  {
    try
    {
      if (segment == null) return;
      var color = TrackPainter.ColorFor(segment);
      if (color.HasValue) __result = color.Value;
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't color a track");
    }
  }
}
