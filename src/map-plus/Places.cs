using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using Track;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal sealed class Place
{
  public Place(string name, Area area, Industry industry, IIndustryTrackDisplayable track, string spanKey)
  {
    Name = name;
    Area = area;
    Industry = industry;
    Track = track;
    SpanKey = spanKey;
  }

  public string Name { get; }
  public Area Area { get; }
  public Industry Industry { get; }
  public IIndustryTrackDisplayable Track { get; }
  public string SpanKey { get; }
  public Vector3 Position => Track.CenterPoint;
  public IEnumerable<TrackSpan> Spans => (Track.TrackSpans ?? Enumerable.Empty<TrackSpan>()).Where(span => span != null);
}

internal static class Places
{
  private static readonly HashSet<string> Skipped = new();

  internal static List<(Area Area, List<Place> Places)> Towns()
  {
    return (OpsController.Shared?.Areas ?? Enumerable.Empty<Area>())
      .Where(area => area != null && !string.IsNullOrEmpty(area.name))
      .Select(area => (Area: area, Places: In(area)))
      .Where(town => town.Places.Count > 0)
      .OrderBy(town => town.Area.name)
      .ToList();
  }

  private static List<Place> In(Area area)
  {
    var places = new List<Place>();
    foreach (var industry in area.Industries.Where(industry => industry.IsEnabledReachable))
    {
      var groups = industry.TrackDisplayables
        .Where(IsAvailable)
        .Select(track => (Track: track, Key: SpanKey(track.TrackSpans)))
        .Where(entry => entry.Key != null)
        .GroupBy(entry => entry.Key);
      foreach (var group in groups)
      {
        var names = group.Select(entry => entry.Track.DisplayName).Distinct().ToList();
        places.Add(new Place(names.Count == 1 ? names[0] : industry.DisplayName, area, industry, group.First().Track, group.Key));
      }
    }
    return places.OrderBy(place => place.Name).ToList();
  }

  private static bool IsAvailable(IIndustryTrackDisplayable track)
  {
    try
    {
      return track switch
      {
        Interchange interchange => interchange.IsVisible && interchange.IsAnySpanReachable && !interchange.Disabled,
        IndustryComponent component => component.IsVisible && component.IsAnySpanReachable,
        PassengerStop stop => stop.IsEnabledReachable,
        _ => false
      };
    }
    catch (Exception exception)
    {
      var name = track?.DisplayName ?? "a location";
      if (Skipped.Add(name)) MapPlusMod.Log?.Warning($"Skipped {name}: {exception.Message}");
      return false;
    }
  }

  internal static string SpanKey(IEnumerable<TrackSpan> spans)
  {
    var keys = (spans ?? Enumerable.Empty<TrackSpan>())
      .Where(span => span != null)
      .Select(span => string.IsNullOrEmpty(span.id) ? span.GetInstanceID().ToString() : span.id)
      .OrderBy(key => key)
      .ToList();
    return keys.Count == 0 ? null : string.Join("|", keys);
  }
}
