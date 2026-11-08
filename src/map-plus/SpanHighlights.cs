using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Railroader.Modding;
using Track;
using UI;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class SpanHighlights
{
  internal static readonly Color HighlightColor = new(1f, 0.86f, 0.2f);
  private static readonly Dictionary<string, List<TrackSegment>> Tokens = new();
  private static readonly Dictionary<string, LitPath> PathTokens = new();

  private static IModLogger _log;
  private static bool _warned;

  internal static int Version { get; private set; }

  internal static bool Active => Tokens.Count > 0;

  internal static int PathVersion { get; private set; }

  internal static bool PathsActive => PathTokens.Count > 0;

  internal static List<LitPath> Paths => PathTokens.Values.ToList();

  internal static List<TrackSegment> Segments => Tokens.Values.SelectMany(segments => segments).Where(segment => segment != null).Distinct().ToList();

  internal static void Patch(Harmony harmony, IModLogger log)
  {
    _log = log;
    try
    {
      var add = AccessTools.Method(typeof(SegmentIndicatorController), "Add", new[] { typeof(IEnumerable<string>) });
      var addSegments = AccessTools.Method(typeof(SegmentIndicatorController), "Add", new[] { typeof(IEnumerable<TrackSegment>) });
      var remove = AccessTools.Method(typeof(SegmentIndicatorController), "Remove", new[] { typeof(string) });
      var addPath = AccessTools.Method(typeof(SegmentIndicatorController), "Add", new[] { typeof(IReadOnlyList<Location>), typeof(Color), typeof(bool) });
      var updatePath = AccessTools.Method(typeof(SegmentIndicatorController), "UpdatePath", new[] { typeof(string), typeof(IReadOnlyList<Location>) });
      if (add == null || remove == null)
      {
        log?.Warning("The game's track highlighter changed, so highlighted destinations don't show on the maps");
        return;
      }
      harmony.Patch(add, postfix: new HarmonyMethod(typeof(SpanHighlights), nameof(AddPostfix)));
      if (addSegments != null) harmony.Patch(addSegments, postfix: new HarmonyMethod(typeof(SpanHighlights), nameof(AddSegmentsPostfix)));
      if (addPath != null) harmony.Patch(addPath, postfix: new HarmonyMethod(typeof(SpanHighlights), nameof(AddPathPostfix)));
      if (updatePath != null) harmony.Patch(updatePath, postfix: new HarmonyMethod(typeof(SpanHighlights), nameof(UpdatePathPostfix)));
      harmony.Patch(remove, prefix: new HarmonyMethod(typeof(SpanHighlights), nameof(RemovePrefix)));
    }
    catch (Exception exception)
    {
      log?.Warning($"Couldn't follow the game's highlighted destinations: {exception.Message}");
    }
  }

  internal static void Reset()
  {
    Tokens.Clear();
    PathTokens.Clear();
    Version++;
    PathVersion++;
  }

  internal static Vector3 Center(List<TrackSegment> segments)
  {
    var sum = Vector3.zero;
    var weight = 0f;
    foreach (var segment in segments)
    {
      var length = Mathf.Max(segment.GetLength(), 0.1f);
      segment.GetPositionRotationAtDistance(length / 2f, TrackSegment.End.A, PositionAccuracy.Standard, out var middle, out _);
      sum += middle * length;
      weight += length;
    }
    return weight > 0f ? sum / weight : sum;
  }

  private static void AddPostfix(IEnumerable<string> spanIds, string __result)
  {
    try
    {
      if (string.IsNullOrEmpty(__result) || spanIds == null) return;
      var graph = Graph.Shared;
      if (graph == null) return;
      var segments = spanIds.Select(graph.SpanForId).Where(span => span != null).SelectMany(span => span.GetSegments()).Where(segment => segment != null).Distinct().ToList();
      if (segments.Count == 0) return;
      Tokens[__result] = segments;
      Version++;
    }
    catch (Exception exception)
    {
      if (_warned) return;
      _warned = true;
      _log?.Warning($"Couldn't follow a highlighted destination: {exception.Message}");
    }
  }

  private static void AddSegmentsPostfix(IEnumerable<TrackSegment> segments, string __result)
  {
    try
    {
      if (string.IsNullOrEmpty(__result) || segments == null) return;
      var list = segments.Where(segment => segment != null).Distinct().ToList();
      if (list.Count == 0) return;
      Tokens[__result] = list;
      Version++;
    }
    catch (Exception exception)
    {
      if (_warned) return;
      _warned = true;
      _log?.Warning($"Couldn't follow a highlighted track: {exception.Message}");
    }
  }

  private static void AddPathPostfix(IReadOnlyList<Location> path, Color tint, string __result)
  {
    try
    {
      if (string.IsNullOrEmpty(__result) || path == null) return;
      PathTokens[__result] = new LitPath(Points(path), tint);
      PathVersion++;
    }
    catch (Exception exception)
    {
      if (_warned) return;
      _warned = true;
      _log?.Warning($"Couldn't follow a highlighted path: {exception.Message}");
    }
  }

  private static void UpdatePathPostfix(string token, IReadOnlyList<Location> path)
  {
    try
    {
      if (token == null || path == null || !PathTokens.TryGetValue(token, out var lit)) return;
      PathTokens[token] = new LitPath(Points(path), lit.Tint);
      PathVersion++;
    }
    catch (Exception exception)
    {
      if (_warned) return;
      _warned = true;
      _log?.Warning($"Couldn't follow a highlighted path: {exception.Message}");
    }
  }

  private static List<Vector3> Points(IReadOnlyList<Location> path)
  {
    var graph = Graph.Shared;
    var points = new List<Vector3>(path.Count);
    if (graph == null) return points;
    foreach (var location in path)
    {
      if (location.segment == null) continue;
      points.Add(graph.GetPosition(location));
    }
    return points;
  }

  private static void RemovePrefix(string token)
  {
    if (token == null) return;
    if (Tokens.Remove(token)) Version++;
    if (PathTokens.Remove(token)) PathVersion++;
  }
}

internal sealed class LitPath
{
  public LitPath(List<Vector3> points, Color tint)
  {
    Points = points;
    Tint = tint;
  }

  public List<Vector3> Points { get; }
  public Color Tint { get; }
}
