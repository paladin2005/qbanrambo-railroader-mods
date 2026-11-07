using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using Track;

namespace QbanRambo.TrainOrders;

internal static class Places
{
  private const float Tolerance = 2f;

  private static readonly Dictionary<string, string> Names = new();

  internal static void Reset() => Names.Clear();

  internal static string NameFor(string locationString)
  {
    if (string.IsNullOrEmpty(locationString)) return null;
    if (Names.TryGetValue(locationString, out var name)) return name;
    name = Find(locationString);
    if (name != null) Names[locationString] = name;
    return name;
  }

  private static string Find(string locationString)
  {
    var ops = OpsController.Shared;
    var graph = Graph.Shared;
    if (ops == null || graph == null) return null;
    Location location;
    try
    {
      location = graph.ResolveLocationString(locationString);
    }
    catch (Exception)
    {
      return null;
    }
    var nearby = new[]
    {
      location,
      graph.Current.LocationByMoving(location, Tolerance, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true),
      graph.Current.LocationByMoving(location, -Tolerance, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true)
    };
    foreach (var candidate in nearby)
    {
      var name = TrackName(ops, candidate);
      if (name != null) return name;
    }
    var area = ops.ClosestAreaForGamePosition(graph.GetPosition(location));
    return area != null && !string.IsNullOrEmpty(area.name) ? $"{area.name} waypoint" : null;
  }

  private static string TrackName(OpsController ops, Location location)
  {
    var matches = ops.Areas
      .Where(area => area != null)
      .SelectMany(area => area.Industries)
      .Where(industry => industry != null)
      .SelectMany(industry => industry.TrackDisplayables
        .Where(track => track != null && IsShown(track) && Contains(track, location))
        .Select(track => (Industry: industry, Track: track)))
      .ToList();
    var names = matches.Select(match => match.Track.DisplayName).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList();
    if (names.Count == 0) return null;
    if (names.Count == 1) return names[0];
    return matches.Select(match => match.Industry).Distinct().Count() == 1 ? matches[0].Industry.DisplayName : names[0];
  }

  private static bool IsShown(IIndustryTrackDisplayable track) => track is not IndustryComponent component || component.IsVisible;

  private static bool Contains(IIndustryTrackDisplayable track, Location location)
  {
    return (track.TrackSpans ?? Enumerable.Empty<TrackSpan>()).Any(span =>
    {
      try
      {
        return span != null && span.Contains(location);
      }
      catch (Exception)
      {
        return false;
      }
    });
  }
}
