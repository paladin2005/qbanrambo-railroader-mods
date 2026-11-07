using System;
using System.Linq;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Model.Ops;
using Track;
using Track.Search;

namespace QbanRambo.Goto;

internal static class DeadEnds
{
  internal const string ChoiceKey = "qbanrambo.goto.dead-end-choice";
  internal const string SmartEntrance = "smart";
  internal const string NormalEntrance = "normal";

  private const float SearchDistance = 300f;
  private const int MaxSegments = 50;

  internal static bool IsDeadEnd(OpsCarPosition position)
  {
    foreach (var span in position.Spans ?? Array.Empty<TrackSpan>())
    {
      if (span == null) continue;
      foreach (var end in new[] { span.lower, span.upper })
      {
        if (end.HasValue && end.Value.segment != null && TryFindExit(end.Value.segment, out _)) return true;
      }
    }
    return false;
  }

  internal static OrderWaypoint Waypoint(BaseLocomotive locomotive, Location target, out string note)
  {
    note = null;
    var plain = new OrderWaypoint(Graph.LocationToString(target), null);
    if (!HasCars(locomotive) || !TryFindExit(target.segment, out var exitSide)) return plain;
    var plan = PosedWaypoint.Plan(locomotive, Graph.Shared.Current, target, null, CarsEnd(locomotive), WaypointPose.ToBodyToward(exitSide));
    if (plan.IsPlanned && plan.Waypoint.HasValue)
    {
      note = "pushing the cars in";
      return plan.Waypoint.Value;
    }
    note = string.IsNullOrEmpty(plan.Message) ? "couldn't push the cars in" : $"couldn't push the cars in: {plan.Message}";
    return plain;
  }

  private static bool TryFindExit(TrackSegment segment, out TrackSegment.End exitSide)
  {
    exitSide = TrackSegment.End.A;
    var deadAtA = LeadsToDeadEnd(segment, TrackSegment.End.A);
    var deadAtB = LeadsToDeadEnd(segment, TrackSegment.End.B);
    if (deadAtA == deadAtB) return false;
    exitSide = deadAtA ? TrackSegment.End.B : TrackSegment.End.A;
    return true;
  }

  private static bool LeadsToDeadEnd(TrackSegment segment, TrackSegment.End toward)
  {
    var graph = Graph.Shared;
    var current = segment;
    var node = segment.NodeForEnd(toward);
    var distance = 0f;
    for (var i = 0; i < MaxSegments && node != null; i++)
    {
      var next = graph.SegmentsConnectedTo(node).Where(other => other != current && IsUsable(other)).ToList();
      if (next.Count == 0) return true;
      if (next.Count > 1) return false;
      current = next[0];
      distance += current.GetLength();
      if (distance > SearchDistance) return false;
      node = current.GetOtherNode(node);
    }
    return false;
  }

  internal static bool HasCars(BaseLocomotive locomotive) => locomotive.EnumerateCoupled().Any(IsCar);

  internal static WaypointEnd CarsEnd(BaseLocomotive locomotive)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var index = cars.IndexOf(locomotive);
    return cars.Take(index).Count(IsCar) > cars.Skip(index + 1).Count(IsCar) ? WaypointEnd.A : WaypointEnd.B;
  }

  private static bool IsCar(Car car) => car is not BaseLocomotive && !car.CarType.IsTender;

  internal static bool IsUsable(TrackSegment segment) => segment.Available && segment.ConstructionState == TrackConstructionState.Complete;
}
