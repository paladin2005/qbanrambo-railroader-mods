using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Model.Ops;
using Track;
using Track.Search;
using UnityEngine;

namespace QbanRambo.Goto;

internal static class Coupling
{
  private const float StandingSpeed = 0.5f;

  internal static List<Car> StandingCars(BaseLocomotive locomotive, OpsCarPosition position)
  {
    var found = new List<Car>();
    var controller = TrainController.Shared;
    if (controller == null) return found;
    var own = new HashSet<Car>(locomotive.EnumerateCoupled());
    var buffer = new List<Car>();
    foreach (var span in position.Spans ?? Array.Empty<TrackSpan>())
    {
      if (span == null) continue;
      buffer.Clear();
      controller.GetCarsOnSpan(span, buffer);
      foreach (var car in buffer)
      {
        if (car == null || own.Contains(car) || found.Contains(car) || Mathf.Abs(car.velocity) >= StandingSpeed) continue;
        found.Add(car);
      }
    }
    return found;
  }

  internal static OrderWaypoint? Plan(BaseLocomotive locomotive, List<Car> standing, WaypointEnd? arrivingEnd, out Car target, out string message)
  {
    target = null;
    message = null;
    var view = Graph.Shared.Current;
    PlacementPlan? best = null;
    foreach (var car in standing)
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
        foreach (var arriving in arrivingEnd.HasValue ? new[] { arrivingEnd.Value } : new[] { WaypointEnd.A, WaypointEnd.B })
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

  internal static void ConnectAir(Car locomotive)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    for (var i = 0; i < cars.Count - 1; i++)
    {
      var front = cars[i];
      var back = cars[i + 1];
      if (!front[Car.LogicalEnd.B].IsAirConnected) StateManager.ApplyLocal(new SetGladhandsConnected(front.id, back.id, true));
      SetAnglecock(front, Car.LogicalEnd.B, true);
      SetAnglecock(back, Car.LogicalEnd.A, true);
    }
    if (cars.Count == 0) return;
    SetAnglecock(cars[0], Car.LogicalEnd.A, false);
    SetAnglecock(cars[cars.Count - 1], Car.LogicalEnd.B, false);
  }

  internal static void SetAnglecock(Car car, Car.LogicalEnd end, bool open)
  {
    var setting = car[end].AnglecockSetting;
    if (open ? setting > 0.99f : setting < 0.01f) return;
    car.ApplyEndGearChange(end, Car.EndGearStateKey.Anglecock, open ? 1f : 0f);
  }
}
