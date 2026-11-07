using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class Pickups
{
  private const float StandingSpeed = 0.5f;
  private const float SearchRadius = 300f;
  private const float MaxRoute = 600f;
  private const float LineReach = 800f;
  private const int MaxSegments = 60;
  private const int MaxGroups = 8;

  internal static OrderWaypoint? Nearest(BaseLocomotive locomotive, OrderStep previous, out Car target, out string problem)
  {
    target = null;
    problem = null;
    var line = Line(locomotive, previous);
    var onLine = Groups(locomotive, (group, _) => group.Any(car => line.Contains(car.LocationA.segment) || line.Contains(car.LocationB.segment)));
    if (onLine.Count > 0)
    {
      var order = Plan(locomotive, Ends(onLine), float.MaxValue, out target, out var message);
      if (order.HasValue) return order;
      problem = message;
    }
    var nearby = Groups(locomotive, (_, distance) => distance <= SearchRadius);
    if (nearby.Count == 0)
    {
      problem ??= "No standing cars on this track or nearby to pick up";
      return null;
    }
    var fallback = Plan(locomotive, Ends(nearby), MaxRoute, out target, out var nearMessage);
    if (!fallback.HasValue) problem = nearMessage ?? problem ?? "Can't reach the cars nearby";
    return fallback;
  }

  internal static OrderWaypoint? CoupleTo(BaseLocomotive locomotive, Car car, out string problem)
  {
    var order = Plan(locomotive, new List<Car> { car }, float.MaxValue, out _, out var message);
    problem = order.HasValue ? null : message ?? $"Can't reach {car.DisplayName}";
    return order;
  }

  internal static OrderWaypoint? CoupleAt(BaseLocomotive locomotive, Car car, Car.End end, WaypointEnd arriving, out string problem)
  {
    problem = null;
    var view = Graph.Shared.Current;
    PlacementPlan plan;
    try
    {
      var mark = PosedWaypoint.CouplerMark(view, car, end);
      plan = PosedWaypoint.Plan(locomotive, view, mark, car.id, arriving, WaypointPose.ToBodyToward(PosedWaypoint.CouplerBodySide(mark)), null, end);
    }
    catch (Exception exception)
    {
      problem = exception.Message;
      return null;
    }
    if (plan.IsPlanned && plan.Waypoint.HasValue) return plan.Waypoint.Value;
    problem = string.IsNullOrEmpty(plan.Message) ? $"Can't couple to {car.DisplayName}" : plan.Message;
    return null;
  }

  internal static List<Car> GroupFrom(BaseLocomotive locomotive, Car target)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var at = cars.IndexOf(target);
    var engine = cars.IndexOf(locomotive);
    if (at < 0 || engine < 0) return new List<Car>();
    return at > engine ? cars.Skip(at).ToList() : cars.Take(at + 1).Reverse().ToList();
  }

  private static HashSet<TrackSegment> Line(BaseLocomotive locomotive, OrderStep previous)
  {
    var line = new HashSet<TrackSegment>();
    var graph = Graph.Shared;
    if (graph == null) return line;
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var seeds = new List<TrackSegment>();
    if (cars.Count > 0)
    {
      seeds.Add(cars[0].LocationA.segment);
      seeds.Add(cars[cars.Count - 1].LocationB.segment);
    }
    if (previous is { Kind: StepKind.Move, Waypoint: { WantsCouple: false } waypoint })
    {
      try
      {
        seeds.Add(graph.ResolveLocationString(waypoint.LocationString).segment);
      }
      catch (Exception)
      {
      }
    }
    foreach (var seed in seeds.Where(seed => seed != null)) Walk(graph, seed, line);
    return line;
  }

  private static void Walk(Graph graph, TrackSegment seed, HashSet<TrackSegment> line)
  {
    line.Add(seed);
    foreach (var end in new[] { TrackSegment.End.A, TrackSegment.End.B })
    {
      var current = seed;
      var node = seed.NodeForEnd(end);
      var distance = 0f;
      for (var i = 0; i < MaxSegments && node != null; i++)
      {
        var next = graph.SegmentsConnectedTo(node).Where(other => other != current && DeadEnds.IsUsable(other)).ToList();
        if (next.Count != 1) break;
        current = next[0];
        if (!line.Add(current)) break;
        distance += current.GetLength();
        if (distance > LineReach) break;
        node = current.GetOtherNode(node);
      }
    }
  }

  private static List<Car> Ends(List<List<Car>> groups) => groups.SelectMany(group => new[] { group[0], group[group.Count - 1] }).Distinct().ToList();

  private static List<List<Car>> Groups(BaseLocomotive locomotive, Func<List<Car>, float, bool> keep)
  {
    var controller = TrainController.Shared;
    var graph = Graph.Shared;
    var groups = new List<(float Distance, List<Car> Cars)>();
    if (controller == null || graph == null) return new List<List<Car>>();
    var own = new HashSet<Car>(locomotive.EnumerateCoupled());
    var seen = new HashSet<Car>();
    var origin = locomotive.GetCenterPosition(graph.Current);
    foreach (var car in controller.Cars)
    {
      if (car == null || own.Contains(car) || seen.Contains(car) || car.IsInBardo) continue;
      var group = car.EnumerateCoupled(Car.LogicalEnd.A).ToList();
      seen.UnionWith(group);
      if (group.Any(member => member is BaseLocomotive || member.CarType.IsTender || Mathf.Abs(member.velocity) >= StandingSpeed)) continue;
      var distance = group.Min(member => Vector3.Distance(origin, member.GetCenterPosition(graph.Current)));
      if (keep(group, distance)) groups.Add((distance, group));
    }
    return groups.OrderBy(entry => entry.Distance).Take(MaxGroups).Select(entry => entry.Cars).ToList();
  }

  private static OrderWaypoint? Plan(BaseLocomotive locomotive, List<Car> candidates, float maxRoute, out Car target, out string message)
  {
    target = null;
    message = null;
    var view = Graph.Shared.Current;
    PlacementPlan? best = null;
    foreach (var car in candidates)
    {
      foreach (var end in new[] { Car.End.F, Car.End.R })
      {
        if (car[car.EndToLogical(end)].IsCoupled) continue;
        Location mark;
        try
        {
          mark = PosedWaypoint.CouplerMark(view, car, end);
        }
        catch (Exception)
        {
          continue;
        }
        foreach (var arriving in new[] { WaypointEnd.A, WaypointEnd.B })
        {
          PlacementPlan plan;
          try
          {
            plan = PosedWaypoint.Plan(locomotive, view, mark, car.id, arriving, WaypointPose.ToBodyToward(PosedWaypoint.CouplerBodySide(mark)), null, end);
          }
          catch (Exception)
          {
            continue;
          }
          if (!plan.IsPlanned || !plan.Waypoint.HasValue)
          {
            message ??= plan.Message;
            continue;
          }
          if (plan.RouteDistance > maxRoute)
          {
            message ??= "The nearest cars are too far to reach";
            continue;
          }
          if (best.HasValue && plan.RouteDistance >= best.Value.RouteDistance) continue;
          best = plan;
          target = car;
        }
      }
    }
    if (!best.HasValue) return null;
    message = null;
    return best.Value.Waypoint;
  }
}
