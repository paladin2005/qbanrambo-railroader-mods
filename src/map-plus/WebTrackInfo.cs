using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Mow;
using Model.Ops;
using Newtonsoft.Json;
using Track;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebTrackInfo
{
  private const float RefreshSeconds = 20f;

  private static byte[] _cache;
  private static float _cachedAt = float.MinValue;
  private static int _cachedVersion = -1;

  internal static void Reset()
  {
    _cache = null;
    _cachedVersion = -1;
  }

  internal static byte[] Build()
  {
    if (!WebMapData.InGame) return Json(new { ok = false, reason = "Load a game to see the map" });
    var index = WebMapData.SegmentIndex;
    var version = WebMapData.BuiltVersion;
    if (_cache != null && _cachedVersion == version && Time.unscaledTime - _cachedAt < RefreshSeconds) return _cache;
    var count = index.Count == 0 ? 0 : index.Values.Max() + 1;
    var placeOf = Enumerable.Repeat(-1, count).ToArray();
    var names = new List<object>();
    foreach (var town in Places.Towns())
    {
      foreach (var place in town.Places)
      {
        var number = names.Count;
        var industry = place.Industry?.DisplayName;
        names.Add(new object[] { place.Name, industry == place.Name ? null : industry, town.Area.name, KindOf(place) });
        foreach (var span in place.Spans)
        {
          foreach (var segment in span.GetSegments())
          {
            if (segment != null && index.TryGetValue(segment, out var value) && value < count && placeOf[value] < 0) placeOf[value] = number;
          }
        }
      }
    }
    var controller = TrackConditionController.Shared;
    var graph = Graph.Shared;
    var rows = new object[count];
    foreach (var entry in index)
    {
      var segment = entry.Key;
      var value = entry.Value;
      if (segment == null || value >= count) continue;
      try
      {
        var condition = controller != null ? TrackInfo.ConditionPercent(controller, segment) : -1;
        var grading = controller != null ? controller.GetGradingClass(segment) : 0;
        var grade = graph != null ? (float)Math.Round(Mathf.Abs(graph.GradeAtLocation(new Location(segment, segment.GetLength() / 2f, TrackSegment.End.A))), 1) : 0f;
        rows[value] = new object[] { segment.EffectiveSpeedLimitMph, grading, condition, grade, placeOf[value] };
      }
      catch (Exception)
      {
        rows[value] = new object[] { 0, 0, -1, 0f, placeOf[value] };
      }
    }
    _cache = Json(new { ok = true, sv = version, s = rows, pl = names });
    _cachedVersion = version;
    _cachedAt = Time.unscaledTime;
    return _cache;
  }

  private static string KindOf(Place place)
  {
    return place.Track switch
    {
      PassengerStop => "station",
      Interchange => "interchange",
      _ => "industry"
    };
  }

  private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
}
