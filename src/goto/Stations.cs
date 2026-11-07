using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.AI;
using Model.Ops;
using Track;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal static class Stations
{
  internal const string StationKey = "qbanrambo.goto.station";

  internal static PassengerStop Find(OpsCarPosition position)
  {
    return (OpsController.Shared?.AllPassengerStops ?? Array.Empty<PassengerStop>()).FirstOrDefault(stop => stop != null && stop.identifier == position.Identifier);
  }

  internal static List<Car> Coaches(Car locomotive) => locomotive.EnumerateCoupled().Where(car => car.IsPassengerCar()).ToList();

  internal static bool StopsEnabled => StateManager.Shared?.Storage == null || StateManager.Shared.Storage.AIPassengerStopEnable;

  internal static Location? Beyond(BaseLocomotive locomotive, PassengerStop stop, Location end)
  {
    var cars = locomotive.EnumerateCoupled().ToList();
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var forward = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Forward;
    var endDistance = RouteDistance(locomotive, cars, length, forward, end) ?? RouteDistance(locomotive, cars, length, !forward, end);
    if (!endDistance.HasValue) return null;
    foreach (var ahead in new[] { true, false })
    {
      var walked = TrackWalk.Ahead(end, ahead, length, false);
      if (!walked.HasValue) continue;
      var candidate = walked.Value;
      if (stop.Spans.Any(span => Contains(span, candidate))) continue;
      var distance = RouteDistance(locomotive, cars, length, forward, candidate) ?? RouteDistance(locomotive, cars, length, !forward, candidate);
      if (distance.HasValue && distance.Value > endDistance.Value + 1f) return candidate;
    }
    return null;
  }

  internal static void Tick(Car locomotive, PassengerStop stop)
  {
    foreach (var coach in Coaches(locomotive))
    {
      var current = coach.GetPassengerMarker()?.Destinations ?? new HashSet<string>();
      if (current.Contains(stop.identifier)) continue;
      StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, current.Append(stop.identifier).ToList()));
    }
  }

  internal static bool IsOn(PassengerStop stop, ICollection<Car> cars)
  {
    var found = new List<Car>();
    foreach (var span in stop.Spans.Where(span => span != null))
    {
      found.Clear();
      TrainController.Shared.GetCarsOnSpan(span, found);
      if (found.Any(cars.Contains)) return true;
    }
    return false;
  }

  private static float? RouteDistance(BaseLocomotive locomotive, List<Car> cars, float length, bool forward, Location location)
  {
    var waypoint = new OrderWaypoint(Graph.LocationToString(location), null);
    return AutoEngineerPlanner.TryPlanRoute(locomotive, cars, length, forward, waypoint, Graph.Shared, out _, out _, out _, out var distance) ? distance : null;
  }

  private static bool Contains(TrackSpan span, Location location)
  {
    try
    {
      return span != null && span.Contains(location);
    }
    catch (Exception)
    {
      return false;
    }
  }
}

internal sealed class StationWatch : MonoBehaviour
{
  private const float TickSeconds = 0.5f;
  private const float SettleSeconds = 2f;
  private const float MovingSpeed = 0.5f;
  private const float TimeoutSeconds = 3600f;
  private const float ResumeDelay = 5f;
  private const float ResumeRetry = 2f;
  private const int ResumeTries = 30;

  private static StationWatch _shared;

  private readonly Dictionary<string, Watch> _watches = new();
  private float _nextTick;
  private bool _resumePending;
  private float _resumeAt;
  private int _resumeTries;

  internal static void Begin(BaseLocomotive locomotive, PassengerStop stop, string locationString)
  {
    Ensure();
    _shared._watches[locomotive.id] = new Watch(locomotive, stop, locationString, Time.unscaledTime);
    StationMemory.Remember(locomotive.id, stop.identifier, locationString);
  }

  internal static void ResumeSoon()
  {
    Ensure();
    _shared._resumePending = true;
    _shared._resumeAt = Time.unscaledTime + ResumeDelay;
    _shared._resumeTries = 0;
  }

  internal static void Clear()
  {
    if (_shared != null)
    {
      _shared._watches.Clear();
      Destroy(_shared.gameObject);
    }
    _shared = null;
  }

  private static void Ensure()
  {
    if (_shared == null) _shared = new GameObject("Goto Station Watch").AddComponent<StationWatch>();
  }

  private void OnDestroy()
  {
    if (_shared == this) _shared = null;
  }

  private void Update()
  {
    TryResume();
    if (Time.unscaledTime < _nextTick) return;
    _nextTick = Time.unscaledTime + TickSeconds;
    foreach (var id in _watches.Keys.ToList())
    {
      try
      {
        var watch = _watches[id];
        if (watch.Locomotive == null)
        {
          _watches.Remove(id);
          continue;
        }
        if (!Check(watch)) continue;
        _watches.Remove(id);
        StationMemory.Forget(id);
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Error(exception, "Couldn't watch the station stop");
        _watches.Remove(id);
        StationMemory.Forget(id);
      }
    }
    if (_watches.Count == 0 && !_resumePending) Destroy(gameObject);
  }

  private void TryResume()
  {
    if (!_resumePending || Time.unscaledTime < _resumeAt) return;
    var controller = TrainController.Shared;
    if ((controller == null || !controller.Cars.Any() || Graph.Shared == null) && ++_resumeTries < ResumeTries)
    {
      _resumeAt = Time.unscaledTime + ResumeRetry;
      return;
    }
    _resumePending = false;
    if (controller == null) return;
    foreach (var record in StationMemory.Load().Values)
    {
      try
      {
        if (_watches.ContainsKey(record.Locomotive)) continue;
        var stop = (OpsController.Shared?.AllPassengerStops ?? Array.Empty<PassengerStop>()).FirstOrDefault(candidate => candidate != null && candidate.identifier == record.Stop);
        if (stop == null || !controller.TryGetCarForId(record.Locomotive, out var car) || car is not BaseLocomotive locomotive || !Waiting(locomotive, record.Location))
        {
          StationMemory.Forget(record.Locomotive);
          continue;
        }
        var coaches = Stations.Coaches(locomotive);
        _watches[locomotive.id] = new Watch(locomotive, stop, record.Location, Time.unscaledTime) { Moved = coaches.Count > 0 && Stations.IsOn(stop, coaches) };
        GotoMenu.Log?.Info($"Resumed the stop of {locomotive.DisplayName} at {stop.DisplayName}");
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Warning($"Couldn't resume the station stop of {record.Locomotive}: {exception.Message}");
      }
    }
  }

  private static bool Waiting(BaseLocomotive locomotive, string locationString)
  {
    var orders = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders;
    return orders.Mode == AutoEngineerMode.Waypoint && orders.Waypoint?.LocationString == locationString;
  }

  private static bool Check(Watch watch)
  {
    var locomotive = watch.Locomotive;
    if (Time.unscaledTime - watch.Started > TimeoutSeconds) return true;
    var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
    var orders = helper.Orders;
    if (orders.Mode != AutoEngineerMode.Waypoint || orders.Waypoint?.LocationString != watch.LocationString) return true;
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed) watch.Moved = true;
    if (!watch.Moved || !locomotive.IsStopped(SettleSeconds)) return false;
    var coaches = Stations.Coaches(locomotive);
    if (coaches.Count == 0 || !Stations.IsOn(watch.Stop, coaches)) return false;
    helper.ClearWaypoint();
    return true;
  }

  private sealed class Watch
  {
    public Watch(BaseLocomotive locomotive, PassengerStop stop, string locationString, float started)
    {
      Locomotive = locomotive;
      Stop = stop;
      LocationString = locationString;
      Started = started;
    }

    public BaseLocomotive Locomotive { get; }
    public PassengerStop Stop { get; }
    public string LocationString { get; }
    public float Started { get; }
    public bool Moved { get; set; }
  }
}
