using System.Linq;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;

namespace QbanRambo.TrainOrders;

internal static class DeadEnds
{
  private const float SearchDistance = 300f;
  private const int MaxSegments = 50;

  internal static bool TrySmartWaypoint(BaseLocomotive locomotive, Location target, out OrderWaypoint waypoint, out string problem)
  {
    waypoint = default;
    problem = null;
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    if (!cars.Any(IsCar) || !TryFindExit(target.segment, out var exitSide)) return false;
    var index = cars.IndexOf(locomotive);
    var carsEnd = cars.Take(index).Count(IsCar) > cars.Skip(index + 1).Count(IsCar) ? WaypointEnd.A : WaypointEnd.B;
    var plan = PosedWaypoint.Plan(locomotive, Graph.Shared.Current, target, null, carsEnd, WaypointPose.ToBodyToward(exitSide));
    if (plan.IsPlanned && plan.Waypoint.HasValue)
    {
      waypoint = plan.Waypoint.Value;
      return true;
    }
    problem = plan.Message;
    return false;
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

  private static bool IsCar(Car car) => car is not BaseLocomotive && !car.CarType.IsTender;

  internal static bool IsUsable(TrackSegment segment) => segment.Available && segment.ConstructionState == TrackConstructionState.Complete;
}
