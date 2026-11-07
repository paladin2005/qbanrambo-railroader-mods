using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Ops;
using Track;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal sealed class TurntableSite
{
  public TurntableSite(Turntable turntable, string name, List<PitTrack> tracks)
  {
    Turntable = turntable;
    Name = name;
    Tracks = tracks;
  }

  public Turntable Turntable { get; }
  public TurntableController Controller => Graph.Shared?.TurntableControllerForId(Turntable.id);
  public string Name { get; }
  public List<PitTrack> Tracks { get; }
  public TrackSegment Bridge => Turntable.BridgeSegment;
  public int Stops => Turntable.subdivisions;
}

internal sealed class PitTrack
{
  public PitTrack(int index, TrackNode node, bool stall, string label, HashSet<TrackSegment> segments)
  {
    Index = index;
    Node = node;
    Stall = stall;
    Label = label;
    Segments = segments;
  }

  public int Index { get; }
  public TrackNode Node { get; }
  public bool Stall { get; }
  public string Label { get; }
  public HashSet<TrackSegment> Segments { get; }
}

internal static class Turntables
{
  private const float WalkDistance = 400f;
  private const int MaxSegments = 60;
  private static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

  internal static List<TurntableSite> Sites()
  {
    var sites = new List<TurntableSite>();
    var graph = Graph.Shared;
    if (graph == null) return sites;
    foreach (var turntable in graph.Turntables)
    {
      try
      {
        var site = SiteFor(graph, turntable);
        if (site != null) sites.Add(site);
      }
      catch (Exception exception)
      {
        TrainOrdersMod.Log?.Warning($"Skipped turntable {turntable?.id} in Train Orders: {exception.Message}");
      }
    }
    return sites;
  }

  internal static TurntableSite Find(string id) => string.IsNullOrEmpty(id) ? null : Sites().FirstOrDefault(site => site.Turntable.id == id);

  internal static string Label(TurntableSite site, int? exit)
  {
    if (!exit.HasValue) return "Turn around";
    return site?.Tracks.FirstOrDefault(track => track.Index == exit.Value)?.Label ?? "Track";
  }

  internal static PitTrack TrackUnder(TurntableSite site, List<Car> cars)
  {
    foreach (var track in site.Tracks)
    {
      if (cars.Any(car => track.Segments.Contains(car.LocationA.segment) || track.Segments.Contains(car.LocationB.segment))) return track;
    }
    return null;
  }

  internal static bool IsOccupied(PitTrack track, HashSet<Car> except)
  {
    var cars = TrainController.Shared?.Cars;
    if (cars == null) return false;
    return cars.Any(car => car != null && !except.Contains(car) && (track.Segments.Contains(car.LocationA.segment) || track.Segments.Contains(car.LocationB.segment)));
  }

  private static TurntableSite SiteFor(Graph graph, Turntable turntable)
  {
    if (turntable?.nodes == null || turntable.BridgeSegment == null || !turntable.IsInitialized) return null;
    var controller = graph.TurntableControllerForId(turntable.id);
    if (controller == null || !DeadEnds.IsUsable(turntable.BridgeSegment)) return null;
    var found = new List<(int Index, TrackNode Node, bool Stall, HashSet<TrackSegment> Segments)>();
    for (var index = 0; index < turntable.nodes.Count; index++)
    {
      var node = turntable.nodes[index];
      if (node == null) continue;
      var segment = graph.SegmentsConnectedTo(node).FirstOrDefault(other => other != turntable.BridgeSegment && DeadEnds.IsUsable(other));
      if (segment == null) continue;
      var segments = new HashSet<TrackSegment>();
      var stall = Walk(graph, node, segment, segments);
      found.Add((index, node, stall, segments));
    }
    if (found.Count == 0) return null;
    var tracks = new List<PitTrack>();
    var stalls = 0;
    var bearings = found.Where(entry => !entry.Stall).GroupBy(entry => Bearing(turntable.Position, entry.Node.Position)).ToDictionary(group => group.Key, group => group.Count());
    var seen = new Dictionary<string, int>();
    foreach (var entry in found)
    {
      string label;
      if (entry.Stall)
      {
        label = $"Stall {++stalls}";
      }
      else
      {
        var bearing = Bearing(turntable.Position, entry.Node.Position);
        seen[bearing] = seen.TryGetValue(bearing, out var count) ? count + 1 : 1;
        label = bearings[bearing] > 1 ? $"Lead {bearing} {seen[bearing]}" : $"Lead {bearing}";
      }
      tracks.Add(new PitTrack(entry.Index, entry.Node, entry.Stall, label, entry.Segments));
    }
    var town = OpsController.Shared?.ClosestAreaForGamePosition(turntable.Position)?.name;
    return new TurntableSite(turntable, string.IsNullOrEmpty(town) ? "Turntable" : $"{town} turntable", tracks);
  }

  private static bool Walk(Graph graph, TrackNode pit, TrackSegment first, HashSet<TrackSegment> segments)
  {
    var current = first;
    var node = first.GetOtherNode(pit);
    var distance = first.GetLength();
    segments.Add(first);
    for (var i = 0; i < MaxSegments && node != null; i++)
    {
      var next = graph.SegmentsConnectedTo(node).Where(other => other != current && DeadEnds.IsUsable(other)).ToList();
      if (next.Count == 0) return true;
      if (next.Count > 1) return false;
      current = next[0];
      segments.Add(current);
      distance += current.GetLength();
      if (distance > WalkDistance) return false;
      node = current.GetOtherNode(node);
    }
    return false;
  }

  private static string Bearing(Vector3 from, Vector3 to)
  {
    var angle = Mathf.Repeat(Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg, 360f);
    return Compass[Mathf.RoundToInt(angle / 45f) % Compass.Length];
  }
}
