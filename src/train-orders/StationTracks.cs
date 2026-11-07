using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using Track;
using Track.Signals;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class StationTracks
{
  private const float WalkLimit = 2000f;
  private const int UnsignalledWeight = 10;

  private static readonly string[] Sides = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
  private static readonly Dictionary<string, List<(string Id, string Label)>> Cache = new();
  private static HashSet<TrackSegment> _signalled;

  internal static void Reset()
  {
    Cache.Clear();
    _signalled = null;
  }

  internal static List<TrackSpan> Spans(PassengerStop stop) => (stop.Spans ?? Array.Empty<TrackSpan>()).Where(span => span != null).ToList();

  internal static List<(string Id, string Label)> Options(PassengerStop stop)
  {
    if (stop == null) return new List<(string, string)>();
    if (Cache.TryGetValue(stop.identifier, out var cached)) return cached;
    var options = new List<(string Id, string Label)>();
    try
    {
      options = Build(stop);
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Warning($"Couldn't sort the tracks of {stop.DisplayName}: {exception.Message}");
    }
    Cache[stop.identifier] = options;
    return options;
  }

  internal static string Label(PassengerStop stop, string spanId)
  {
    if (string.IsNullOrEmpty(spanId)) return "Shortest route";
    return Options(stop).FirstOrDefault(option => option.Id == spanId).Label ?? "Shortest route";
  }

  internal static List<TrackSpan> Chosen(PassengerStop stop, string spanId)
  {
    var spans = Spans(stop);
    if (string.IsNullOrEmpty(spanId)) return spans;
    var chosen = spans.Where(span => span.id == spanId).ToList();
    return chosen.Count > 0 ? chosen : spans;
  }

  private static List<(string Id, string Label)> Build(PassengerStop stop)
  {
    var spans = Spans(stop).Where(span => !string.IsNullOrEmpty(span.id)).ToList();
    if (spans.Count < 2) return new List<(string, string)>();
    var graph = Graph.Shared;
    var centers = spans.ToDictionary(span => span, span => Center(graph, span));
    var mean = centers.Values.Aggregate(Vector3.zero, (sum, center) => sum + center) / centers.Count;
    var along = Along(graph, spans[0]);
    var across = new Vector3(along.z, 0f, -along.x);
    var sides = spans.ToDictionary(span => span, span =>
    {
      var offset = Vector3.Dot(centers[span] - mean, across);
      return Side(offset >= 0f ? across : -across);
    });
    var scores = spans.ToDictionary(span => span, span => Score(graph, span));
    var known = spans.Count == 2 && scores[spans[0]] != scores[spans[1]];
    var labels = spans.ToDictionary(span => span, span =>
    {
      if (!known) return $"{Capitalize(sides[span])} track";
      var main = scores[span] == scores.Values.Min();
      return main ? $"Main track ({sides[span]})" : $"Siding ({sides[span]})";
    });
    foreach (var group in labels.GroupBy(pair => pair.Value).Where(group => group.Count() > 1).ToList())
    {
      var number = 0;
      foreach (var pair in group.ToList()) labels[pair.Key] = $"{pair.Value} {++number}";
    }
    return spans
      .OrderBy(span => known ? scores[span] : 0)
      .Select(span => (span.id, labels[span]))
      .ToList();
  }

  private static int Score(Graph graph, TrackSpan span)
  {
    var segments = new HashSet<TrackSegment>(span.GetSegments().Where(segment => segment != null));
    if (segments.Count == 0) return 0;
    var score = segments.Any(Signalled().Contains) ? 0 : UnsignalledWeight;
    foreach (var segment in segments)
    {
      foreach (var end in new[] { TrackSegment.End.A, TrackSegment.End.B })
      {
        var node = segment.NodeForEnd(end);
        if (node == null) continue;
        var outside = graph.SegmentsConnectedTo(node).Where(other => other != null && !segments.Contains(other)).ToList();
        if (outside.Count == 0) continue;
        if (graph.IsSwitch(node))
        {
          if (Diverging(graph, node, segment)) score++;
          continue;
        }
        foreach (var next in outside)
        {
          if (ArrivesDiverging(graph, next, next.GetOtherNode(node))) score++;
        }
      }
    }
    return score;
  }

  private static bool ArrivesDiverging(Graph graph, TrackSegment segment, TrackNode node)
  {
    var distance = segment.GetLength();
    while (node != null && distance < WalkLimit)
    {
      if (graph.IsSwitch(node)) return Diverging(graph, node, segment);
      var next = graph.SegmentsConnectedTo(node).Where(other => other != segment).ToList();
      if (next.Count != 1) return false;
      segment = next[0];
      distance += segment.GetLength();
      node = segment.GetOtherNode(node);
    }
    return false;
  }

  private static bool Diverging(Graph graph, TrackNode node, TrackSegment segment)
  {
    return graph.DecodeSwitchAt(node, out _, out _, out var reversed) && reversed == segment;
  }

  private static HashSet<TrackSegment> Signalled()
  {
    if (_signalled != null) return _signalled;
    _signalled = new HashSet<TrackSegment>();
    var blocks = CTCPanelController.Shared?.AllBlocks;
    if (blocks == null) return _signalled;
    foreach (var block in blocks.Values)
    {
      if (block == null) continue;
      foreach (var span in block.GetComponentsInChildren<TrackSpan>())
      {
        foreach (var segment in span.GetSegments())
        {
          if (segment != null) _signalled.Add(segment);
        }
      }
    }
    return _signalled;
  }

  private static Vector3 Center(Graph graph, TrackSpan span)
  {
    var points = new[] { span.lower, span.upper }.Where(location => location.HasValue).Select(location => graph.GetPosition(location.Value)).ToList();
    return points.Count == 0 ? Vector3.zero : points.Aggregate(Vector3.zero, (sum, point) => sum + point) / points.Count;
  }

  private static Vector3 Along(Graph graph, TrackSpan span)
  {
    if (!span.lower.HasValue || !span.upper.HasValue) return Vector3.forward;
    var direction = graph.GetPosition(span.upper.Value) - graph.GetPosition(span.lower.Value);
    direction.y = 0f;
    return direction.sqrMagnitude < 0.01f ? Vector3.forward : direction.normalized;
  }

  private static string Side(Vector3 direction)
  {
    var angle = Mathf.Repeat(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 360f);
    return Sides[Mathf.RoundToInt(angle / 45f) % Sides.Length];
  }

  private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
}
