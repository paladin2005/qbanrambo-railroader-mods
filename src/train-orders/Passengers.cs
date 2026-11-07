using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.AI;
using Model.Ops;
using Track;

namespace QbanRambo.TrainOrders;

internal static class Passengers
{
  internal static List<Car> Coaches(Car locomotive) => locomotive.EnumerateCoupled().Where(car => car.IsPassengerCar()).ToList();

  internal static bool StopsEnabled => StateManager.Shared.Storage.AIPassengerStopEnable;

  internal static List<PassengerStop> Available()
  {
    return Stops().Values.Where(stop => stop.IsEnabledReachable).OrderBy(stop => stop.DisplayName).ToList();
  }

  internal static PassengerStop Stop(string id) => id != null && Stops().TryGetValue(id, out var stop) ? stop : null;

  internal static string Name(string id) => Stop(id)?.DisplayName ?? "station";

  internal static void Keep(Car locomotive, IReadOnlyCollection<string> stations, IReadOnlyCollection<string> listed)
  {
    if (listed.Count == 0) return;
    var coaches = Coaches(locomotive);
    if (coaches.Count == 0) return;
    var stops = Stops();
    var train = new HashSet<Car>(locomotive.EnumerateCoupled());
    var here = new HashSet<string>(stations.Where(id => stops.TryGetValue(id, out var stop) && IsOn(stop, train)));
    var behind = new HashSet<string>(listed.Except(stations));
    foreach (var coach in coaches)
    {
      var current = coach.GetPassengerMarker()?.Destinations ?? new HashSet<string>();
      var missing = stations.Where(id => stops.ContainsKey(id) && !here.Contains(id) && !current.Contains(id)).ToList();
      if (missing.Count == 0 && !current.Any(behind.Contains)) continue;
      StateManager.ApplyLocal(new SetPassengerDestinations(coach.id, current.Where(id => !behind.Contains(id)).Concat(missing).ToList()));
    }
  }

  internal static bool IsOn(PassengerStop stop, ICollection<Car> train) => IsOn(StationTracks.Spans(stop), train);

  internal static bool IsOn(IEnumerable<TrackSpan> spans, ICollection<Car> train)
  {
    var cars = new List<Car>();
    foreach (var span in spans.Where(span => span != null))
    {
      cars.Clear();
      TrainController.Shared.GetCarsOnSpan(span, cars);
      if (cars.Any(train.Contains)) return true;
    }
    return false;
  }

  internal static Location? Target(BaseLocomotive locomotive, PassengerStop stop, out string failure) => Target(locomotive, stop, StationTracks.Spans(stop), false, out failure);

  internal static Location? Target(BaseLocomotive locomotive, PassengerStop stop, IReadOnlyList<TrackSpan> spans, bool keepTrack, out string failure)
  {
    failure = null;
    var cars = locomotive.EnumerateCoupled().ToList();
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var forward = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Forward;
    Location? best = null;
    var bestFar = 0f;
    var bestNear = float.MaxValue;
    foreach (var span in spans)
    {
      if (span == null) continue;
      var reachable = new List<(Location Location, float Distance)>();
      foreach (var end in new[] { span.lower, span.upper })
      {
        if (!end.HasValue) continue;
        var distance = RouteDistance(locomotive, cars, length, forward, end.Value, ref failure) ?? RouteDistance(locomotive, cars, length, !forward, end.Value, ref failure);
        if (distance.HasValue) reachable.Add((end.Value, distance.Value));
      }
      if (reachable.Count == 0) continue;
      var near = reachable.Min(candidate => candidate.Distance);
      if (near >= bestNear) continue;
      bestNear = near;
      var far = reachable.OrderByDescending(candidate => candidate.Distance).First();
      best = far.Location;
      bestFar = far.Distance;
    }
    if (!best.HasValue || Coaches(locomotive).Count == 0) return best;
    return Beyond(locomotive, cars, length, forward, stop, best.Value, bestFar, keepTrack) ?? best;
  }

  private static Location? Beyond(BaseLocomotive locomotive, List<Car> cars, float length, bool forward, PassengerStop stop, Location end, float endDistance, bool keepTrack)
  {
    string ignored = null;
    foreach (var ahead in new[] { true, false })
    {
      var walked = TrackWalk.Ahead(end, ahead, length, keepTrack);
      if (!walked.HasValue) continue;
      var candidate = walked.Value;
      if (stop.Spans.Any(span => Contains(span, candidate))) continue;
      var distance = RouteDistance(locomotive, cars, length, forward, candidate, ref ignored) ?? RouteDistance(locomotive, cars, length, !forward, candidate, ref ignored);
      if (distance.HasValue && distance.Value > endDistance + 1f) return candidate;
    }
    return null;
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

  internal static int MinimumStopSeconds => StateManager.Shared.Storage.AIPassengerStopMinimumStopDuration;

  internal static bool AtAnyStation(Car locomotive)
  {
    var coaches = Coaches(locomotive);
    if (coaches.Count == 0) return false;
    var set = new HashSet<Car>(coaches);
    return Stops().Values.Any(stop => stop.IsEnabledReachable && IsOn(stop, set));
  }

  internal static string Signature(Car locomotive)
  {
    return string.Join("|", Coaches(locomotive).Select(coach =>
    {
      var marker = coach.GetPassengerMarker();
      return marker.HasValue ? $"{coach.id}:{marker.Value.TotalPassengers}:{marker.Value.Groups.Count}" : coach.id;
    }));
  }

  private static float? RouteDistance(BaseLocomotive locomotive, List<Car> cars, float length, bool forward, Location location, ref string failure)
  {
    var waypoint = new OrderWaypoint(Graph.LocationToString(location), null);
    if (AutoEngineerPlanner.TryPlanRoute(locomotive, cars, length, forward, waypoint, Graph.Shared, out _, out _, out var message, out var distance)) return distance;
    failure ??= message;
    return null;
  }

  private static Dictionary<string, PassengerStop> Stops()
  {
    return (OpsController.Shared?.Areas ?? Enumerable.Empty<Area>())
      .Where(area => area != null)
      .SelectMany(area => area.Industries)
      .Select(industry => industry?.PassengerStop)
      .Where(stop => stop != null && !string.IsNullOrEmpty(stop.identifier))
      .GroupBy(stop => stop.identifier)
      .ToDictionary(group => group.Key, group => group.First());
  }
}
