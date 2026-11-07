using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI.Waypoints;
using Track;

namespace QbanRambo.Goto;

internal static class RunAround
{
  internal const string NoBypass = "run arounds need a siding or loop beside the cars, with a switch at each end";

  private const float Clearance = 15f;
  private const float SideReach = 1500f;
  private const float BypassReach = 3000f;
  private const int MaxCandidates = 8;
  private const int MaxSegments = 400;

  private readonly struct Merge
  {
    public Merge(TrackNode node, TrackSegment other, TrackSegment enter, float distance)
    {
      Node = node;
      Other = other;
      Enter = enter;
      Distance = distance;
    }

    public TrackNode Node { get; }
    public TrackSegment Other { get; }
    public TrackSegment Enter { get; }
    public float Distance { get; }
  }

  internal static bool CanGoAround(List<Car> group, Car far, Car.End farEnd, float trainLength, out string problem)
  {
    problem = NoBypass;
    var graph = Graph.Shared;
    if (graph == null || group.Count == 0 || !group.Contains(far)) return false;
    var view = graph.Current;
    var near = NearEnd(group, far, farEnd, out var nearEnd);
    if (near == null) return false;
    Location farMark;
    Location nearMark;
    try
    {
      farMark = PosedWaypoint.CouplerMark(view, far, farEnd);
      nearMark = PosedWaypoint.CouplerMark(view, near, nearEnd);
    }
    catch (Exception)
    {
      return false;
    }
    var blocked = new HashSet<TrackSegment>();
    foreach (var car in group)
    {
      if (car.LocationA.segment != null) blocked.Add(car.LocationA.segment);
      if (car.LocationB.segment != null) blocked.Add(car.LocationB.segment);
    }
    var need = trainLength + Clearance;
    foreach (var first in Merges(graph, nearMark, SideReach + trainLength))
    {
      if (blocked.Contains(first.Other) || Room(graph, first.Node, first.Enter) < need) continue;
      foreach (var second in Merges(graph, farMark, SideReach))
      {
        if (second.Node == first.Node || blocked.Contains(second.Other) || Room(graph, second.Node, second.Enter) < need) continue;
        if (!Bypass(graph, first, second, blocked)) continue;
        problem = null;
        return true;
      }
    }
    return false;
  }

  internal static Car.End OuterEnd(List<Car> group, Car far, BaseLocomotive locomotive)
  {
    var ordered = Ordered(group, far);
    if (ordered.Count > 1) return far.LogicalToEnd(ordered[0] == far ? Car.LogicalEnd.A : Car.LogicalEnd.B);
    var free = new[] { Car.End.F, Car.End.R }.Where(end => !far[far.EndToLogical(end)].IsCoupled).ToList();
    if (free.Count == 1) return free[0];
    var view = Graph.Shared.Current;
    var origin = locomotive.GetCenterPosition(view);
    return free.Count == 0 ? Car.End.F : free.OrderByDescending(end =>
    {
      try
      {
        return UnityEngine.Vector3.Distance(Graph.Shared.GetPosition(PosedWaypoint.CouplerMark(view, far, end)), origin);
      }
      catch (Exception)
      {
        return 0f;
      }
    }).First();
  }

  private static List<Car> Ordered(List<Car> group, Car far)
  {
    var consist = far.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var set = new HashSet<Car>(group);
    return consist.Where(set.Contains).ToList();
  }

  private static Car NearEnd(List<Car> group, Car far, Car.End farEnd, out Car.End nearEnd)
  {
    var ordered = Ordered(group, far);
    nearEnd = Car.End.F;
    if (ordered.Count == 0) return null;
    if (ordered.Count == 1)
    {
      nearEnd = farEnd == Car.End.F ? Car.End.R : Car.End.F;
      return far;
    }
    var near = ordered[0] == far ? ordered[ordered.Count - 1] : ordered[0];
    nearEnd = near.LogicalToEnd(near == ordered[0] ? Car.LogicalEnd.A : Car.LogicalEnd.B);
    return near;
  }

  private static List<Merge> Merges(Graph graph, Location start, float reach)
  {
    var found = new List<Merge>();
    var segment = start.segment;
    if (segment == null) return found;
    var travelled = start.DistanceUntilEnd();
    var node = segment.NodeForEnd(start.EndIsA ? TrackSegment.End.B : TrackSegment.End.A);
    for (var i = 0; i < MaxSegments && node != null && travelled < reach && found.Count < MaxCandidates; i++)
    {
      TrackSegment next;
      if (graph.IsSwitch(node) && graph.DecodeSwitchAt(node, out var enter, out var normal, out var reversed))
      {
        if (segment == enter)
        {
          next = normal;
        }
        else
        {
          found.Add(new Merge(node, segment == normal ? reversed : normal, enter, travelled));
          next = enter;
        }
      }
      else
      {
        var others = graph.SegmentsConnectedTo(node).Where(other => other != segment).ToList();
        next = others.Count == 1 ? others[0] : null;
      }
      if (next == null || !Usable(next)) break;
      travelled += next.GetLength();
      segment = next;
      node = next.GetOtherNode(node);
    }
    return found;
  }

  private static bool Bypass(Graph graph, Merge first, Merge second, HashSet<TrackSegment> blocked)
  {
    if (!Usable(first.Other)) return false;
    var froms = new Dictionary<TrackSegment, TrackNode> { [first.Other] = first.Node };
    var distances = new Dictionary<TrackSegment, float> { [first.Other] = first.Other.GetLength() };
    var queue = new Queue<TrackSegment>();
    queue.Enqueue(first.Other);
    var steps = 0;
    while (queue.Count > 0 && steps++ < MaxSegments)
    {
      var segment = queue.Dequeue();
      var node = segment.GetOtherNode(froms[segment]);
      if (node == null) continue;
      if (node == second.Node)
      {
        if (segment == second.Other) return true;
        continue;
      }
      foreach (var next in Onward(graph, node, segment))
      {
        if (next == null || froms.ContainsKey(next) || blocked.Contains(next) || !Usable(next)) continue;
        var distance = distances[segment] + next.GetLength();
        if (distance > BypassReach) continue;
        froms[next] = node;
        distances[next] = distance;
        queue.Enqueue(next);
      }
    }
    return false;
  }

  private static IEnumerable<TrackSegment> Onward(Graph graph, TrackNode node, TrackSegment from)
  {
    if (graph.IsSwitch(node) && graph.DecodeSwitchAt(node, out var enter, out var normal, out var reversed))
    {
      return from == enter ? new[] { normal, reversed } : new[] { enter };
    }
    return graph.SegmentsConnectedTo(node).Where(other => other != from);
  }

  private static float Room(Graph graph, TrackNode node, TrackSegment segment)
  {
    var room = 0f;
    for (var i = 0; i < MaxSegments && segment != null && Usable(segment); i++)
    {
      room += segment.GetLength();
      var next = node == null ? null : segment.GetOtherNode(node);
      if (next == null || room > SideReach) return room;
      TrackSegment onward;
      if (graph.IsSwitch(next) && graph.DecodeSwitchAt(next, out var enter, out var normal, out _))
      {
        onward = segment == enter ? normal : enter;
      }
      else
      {
        var others = graph.SegmentsConnectedTo(next).Where(other => other != segment).ToList();
        onward = others.Count == 1 ? others[0] : null;
      }
      node = next;
      segment = onward;
    }
    return room;
  }

  private static bool Usable(TrackSegment segment) => segment.Available && segment.ConstructionState == TrackConstructionState.Complete;
}
