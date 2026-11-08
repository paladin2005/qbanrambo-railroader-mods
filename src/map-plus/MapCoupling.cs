using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class MapCoupling
{
  private const string CapturedKey = "qbanrambo.train-orders.captured";
  private const string OrdersHookKey = "qbanrambo.train-orders.web";
  private const string WatchedKey = "qbanrambo.couple-watched";
  private const float StandingSpeed = 0.5f;

  internal static string Unavailable(BaseLocomotive locomotive, Car car)
  {
    if (locomotive == null) return "Select a locomotive first";
    if (car == null) return "No car there";
    if (Consist.Of(locomotive).Contains(car)) return "Already in its train";
    if (Mathf.Abs(car.velocity) >= StandingSpeed) return "The car is moving";
    return null;
  }

  internal static string CoupleTo(BaseLocomotive locomotive, Car car, out bool sent, bool queue = false)
  {
    sent = false;
    var reason = Unavailable(locomotive, car);
    if (reason != null) return reason;
    var group = Consist.Of(car).ToList();
    if (group.Count == 0) group.Add(car);
    var order = Plan(locomotive, group, out var target, out var message);
    var name = group.Count > 1 ? $"{car.DisplayName} ({group.Count} cars)" : car.DisplayName;
    if (!order.HasValue) return message != null ? $"{locomotive.DisplayName} can't couple to {name}: {message}" : $"{locomotive.DisplayName} can't reach {name}";
    var hook = queue ? AppDomain.CurrentDomain.GetData(OrdersHookKey) as Func<string, string, string, string> : null;
    if (queue && hook == null) return "Train Orders isn't installed";
    var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
    bool captured;
    AppDomain.CurrentDomain.SetData(CapturedKey, null);
    AppDomain.CurrentDomain.SetData(WatchedKey, true);
    try
    {
      hook?.Invoke(locomotive.id, "capture", "{\"on\":true}");
      helper.SetWaypointOrder(order.Value);
      captured = AppDomain.CurrentDomain.GetData(CapturedKey) is true;
    }
    finally
    {
      hook?.Invoke(locomotive.id, "capture", "{\"on\":false}");
      AppDomain.CurrentDomain.SetData(CapturedKey, null);
      AppDomain.CurrentDomain.SetData(WatchedKey, null);
    }
    if (queue && !captured) return $"{locomotive.DisplayName}: update Train Orders to add couplings from the web map";
    sent = true;
    if (captured) return $"{locomotive.DisplayName}: coupling to {name} was added to its orders";
    MapCoupleWatch.Begin(locomotive, target, order.Value.LocationString);
    var released = MapWaypoints.ReleaseHandbrakes(locomotive);
    var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
    return $"{locomotive.DisplayName} heading to couple to {name}{handbrakes}";
  }

  private static OrderWaypoint? Plan(BaseLocomotive locomotive, List<Car> group, out Car target, out string message)
  {
    target = null;
    message = null;
    var view = Graph.Shared.Current;
    PlacementPlan? best = null;
    foreach (var car in group)
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
    if (locomotive?.set == null) return;
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

  private static void SetAnglecock(Car car, Car.LogicalEnd end, bool open)
  {
    var setting = car[end].AnglecockSetting;
    if (open ? setting > 0.99f : setting < 0.01f) return;
    car.ApplyEndGearChange(end, Car.EndGearStateKey.Anglecock, open ? 1f : 0f);
  }
}

internal sealed class MapCoupleWatch : MonoBehaviour
{
  private const float TickSeconds = 0.5f;
  private const float SettleSeconds = 2f;
  private const float OrderSettleSeconds = 2f;
  private const float TimeoutSeconds = 1800f;

  private static MapCoupleWatch _shared;

  private readonly Dictionary<string, Job> _jobs = new();
  private float _nextTick;

  internal static void Begin(BaseLocomotive locomotive, Car car, string locationString)
  {
    if (_shared == null) _shared = new GameObject("Map Plus Couple Watch").AddComponent<MapCoupleWatch>();
    _shared._jobs[locomotive.id] = new Job(locomotive, car, locationString, Consist.Of(locomotive).Count());
  }

  internal static void Clear()
  {
    if (_shared != null)
    {
      _shared._jobs.Clear();
      Destroy(_shared.gameObject);
    }
    _shared = null;
  }

  private void OnDestroy()
  {
    if (_shared == this) _shared = null;
  }

  private void Update()
  {
    if (Time.unscaledTime < _nextTick) return;
    _nextTick = Time.unscaledTime + TickSeconds;
    foreach (var id in _jobs.Keys.ToList())
    {
      try
      {
        if (Check(_jobs[id])) _jobs.Remove(id);
      }
      catch (Exception exception)
      {
        MapPlusMod.Log?.Error(exception, "Couldn't finish the coupling");
        _jobs.Remove(id);
      }
    }
    if (_jobs.Count == 0) Destroy(gameObject);
  }

  private static bool Check(Job job)
  {
    var locomotive = job.Locomotive;
    if (locomotive == null || job.Car == null || Time.time - job.Started > TimeoutSeconds) return true;
    if (!Consist.Of(locomotive).Contains(job.Car))
    {
      if (Time.time - job.Started <= OrderSettleSeconds) return false;
      var orders = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders;
      var current = orders.Waypoint?.LocationString;
      return orders.Mode != AutoEngineerMode.Waypoint || (current != null && current != job.LocationString);
    }
    if (!locomotive.IsStopped(SettleSeconds)) return false;
    MapCoupling.ConnectAir(locomotive);
    var added = Consist.Of(locomotive).Count() - job.CarsBefore;
    var released = MapWaypoints.ReleaseHandbrakes(locomotive);
    var cars = added == 1 ? "1 car" : $"{Mathf.Max(added, 1)} cars";
    var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
    Toast.Present($"{locomotive.DisplayName} coupled to {cars}, air connected{handbrakes}", ToastPosition.Bottom);
    return true;
  }

  private sealed class Job
  {
    public Job(BaseLocomotive locomotive, Car car, string locationString, int carsBefore)
    {
      Locomotive = locomotive;
      Car = car;
      LocationString = locationString;
      CarsBefore = carsBefore;
      Started = Time.time;
    }

    public BaseLocomotive Locomotive { get; }
    public Car Car { get; }
    public string LocationString { get; }
    public int CarsBefore { get; }
    public float Started { get; }
  }
}
