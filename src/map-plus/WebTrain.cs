using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Ops;
using Newtonsoft.Json;
using Track;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebTrain
{
  internal const string Header = "X-Map-Plus-Train";

  internal const string OrdersKey = "qbanrambo.train-orders";
  private const float PoundsPerTon = 2000f;
  private const float MetersToFeet = 3.28084f;
  private const float MetersPerSecondToMph = 2.23694f;
  private const float NearbyMeters = 2f;
  private const int AllCars = 0;
  private const int AlwaysRefuel = 100;

  private static readonly Dictionary<string, string> PlaceNames = new();

  internal static void Reset() => PlaceNames.Clear();

  internal static byte[] Build(string id)
  {
    if (!WebMapData.InGame) return Json(new { ok = false, reason = "Load a game to see the map" });
    if (string.IsNullOrEmpty(id) || !TrainController.Shared.TryGetCarForId(id, out var found) || found is not BaseLocomotive lead)
    {
      return Json(new { ok = false, reason = "That train is no longer on the map" });
    }
    var consist = Consist.Of(lead).ToList();
    var engines = consist.OfType<BaseLocomotive>().ToList();
    var cars = consist.Where(car => car is not BaseLocomotive && !car.CarType.IsTender).ToList();
    var persistence = new AutoEngineerPersistence(lead.KeyValueObject);
    var orders = persistence.Orders;
    var mph = lead.velocity * MetersPerSecondToMph;
    var loaded = cars.Count(IsLoaded);
    var destinations = cars
      .Select(CarInfo.Destination)
      .Where(destination => destination != null && !destination.Arrived)
      .GroupBy(destination => destination.Name)
      .OrderByDescending(group => group.Count())
      .Select(group => new { n = group.Key, c = group.Count() })
      .ToList();
    return Json(new
    {
      ok = true,
      i = lead.id,
      n = lead.DisplayName,
      speed = Mathf.RoundToInt(Mathf.Abs(mph)),
      moving = Mathf.Abs(mph) < 0.5f ? "Stopped" : mph > 0f ? "Forward" : "Reverse",
      ae = new
      {
        mode = orders.Mode == AutoEngineerMode.Off ? "Manual" : orders.Mode.ToString(),
        on = orders.Mode != AutoEngineerMode.Off,
        forward = orders.Forward,
        max = orders.MaxSpeedMph,
        status = persistence.PlannerStatus,
        waypoint = orders.Mode == AutoEngineerMode.Waypoint && orders.Waypoint.HasValue ? WaypointName(orders.Waypoint.Value) : null
      },
      alerts = TrainAlerts.For(lead).Select(alert => new { t = alert.Text, c = Hex(TrainAlerts.ColorFor(alert.Level)) }).ToList(),
      summary = new
      {
        cars = cars.Count,
        engines = engines.Count,
        loaded,
        empty = cars.Count - loaded,
        length = Mathf.RoundToInt((consist.Sum(car => car.carLength) + Mathf.Max(0, consist.Count - 1)) * MetersToFeet),
        weight = Mathf.RoundToInt(consist.Sum(car => car.Weight) / PoundsPerTon),
        handbrakes = consist.Count(car => car.air != null && car.air.handbrakeApplied),
        hotboxes = consist.Count(car => car.HasHotbox),
        airGaps = AirGaps(consist),
        destinations
      },
      engines = engines.Select(Engine).ToList(),
      consist = consist.Select(Row).ToList(),
      orders = TrainOrders(lead)
    });
  }

  private static bool IsLoaded(Car car)
  {
    var slots = car.Definition?.LoadSlots;
    if (slots == null) return false;
    for (var index = 0; index < slots.Count; index++)
    {
      var info = car.GetLoadInfo(index);
      if (info.HasValue && !string.IsNullOrEmpty(info.Value.LoadId) && info.Value.Quantity > 0.001f) return true;
    }
    return false;
  }

  private static int AirGaps(List<Car> consist)
  {
    var ends = 0;
    foreach (var car in consist)
    {
      foreach (var gear in new[] { car.EndGearA, car.EndGearB })
      {
        if (gear != null && gear.IsCoupled && !gear.IsAirConnectedAndOpen) ends++;
      }
    }
    return (ends + 1) / 2;
  }

  private static object Engine(BaseLocomotive locomotive)
  {
    var data = new Dictionary<string, object>
    {
      ["i"] = locomotive.id,
      ["n"] = locomotive.DisplayName,
      ["model"] = locomotive.DefinitionInfo?.Metadata?.Name,
      ["kind"] = locomotive is SteamLocomotive ? "Steam" : locomotive is DieselLocomotive ? "Diesel" : "Locomotive",
      ["te"] = Mathf.RoundToInt(locomotive.RatedTractiveEffort),
      ["weight"] = Mathf.RoundToInt(locomotive.Weight / PoundsPerTon),
      ["adhesive"] = Mathf.RoundToInt(locomotive.AdhesiveWeight / PoundsPerTon),
      ["condition"] = Mathf.RoundToInt(locomotive.Condition * 100f),
      ["fuel"] = CarInfo.FuelLevels(locomotive).Select(level => new { n = level.Name, p = level.Percent }).ToList()
    };
    var control = locomotive.locomotiveControl;
    if (control != null)
    {
      data["throttle"] = Mathf.RoundToInt(control.AbstractThrottle * 100f);
      data["notch"] = control.ThrottleDisplay;
      data["reverser"] = Mathf.RoundToInt(control.AbstractReverser * 100f);
      data["trainBrake"] = Mathf.RoundToInt(control.TrainBrakeSetting * 100f);
      data["engineBrake"] = Mathf.RoundToInt(control.LocomotiveBrakeSetting * 100f);
    }
    if (locomotive is SteamLocomotive steam && steam.engine != null)
    {
      var engine = steam.engine;
      data["cylinders"] = $"{engine.numberOfCylinders} × {engine.pistonDiameterInches:0.#} × {engine.pistonStrokeInches:0.#} in";
      data["drivers"] = $"{engine.driverDiameterInches:0.#} in";
      data["boiler"] = $"{engine.pressure:0} of {engine.maximumBoilerPressure:0} psi";
      data["maxSpeed"] = Mathf.RoundToInt(engine.MaximumSpeedMph);
    }
    return data;
  }

  private static object Row(Car car)
  {
    var kind = car is BaseLocomotive ? "engine" : car.CarType.IsTender ? "tender" : "car";
    return new
    {
      i = car.id,
      n = car.DisplayName,
      model = car.DefinitionInfo?.Metadata?.Name,
      k = kind,
      len = Mathf.RoundToInt(car.carLength * MetersToFeet),
      w = Mathf.RoundToInt(car.Weight / PoundsPerTon),
      load = kind == "car" ? CarInfo.Loads(car) : null,
      dest = kind == "car" ? CarInfo.Destination(car)?.Describe() : null,
      color = kind == "car" && car.CarType.IsFreight ? Hex(CarInfo.MapColor(car)) : null,
      hb = car.air != null && car.air.handbrakeApplied,
      hot = car.HasHotbox,
      derailed = car.IsDerailed,
      oil = car.EnableOiling ? Mathf.RoundToInt(car.Oiled * 100f) : (int?)null,
      cond = Mathf.RoundToInt(car.Condition * 100f)
    };
  }

  private static object TrainOrders(BaseLocomotive locomotive)
  {
    Value value;
    try
    {
      value = locomotive.KeyValueObject[OrdersKey];
    }
    catch (Exception)
    {
      return null;
    }
    if (value.IsNull) return null;
    var stepValues = value["steps"];
    var steps = stepValues.IsNull ? new List<Value>() : stepValues.ArrayValue.ToList();
    if (steps.Count == 0) return null;
    var at = value["at"].IntValueOrDefault(0);
    var running = value["run"].BoolValueOrDefault(false);
    return new
    {
      running,
      status = value["status"].StringValueOrDefault(null),
      repeat = value["repeat"].BoolValueOrDefault(false),
      smart = value["pass"].BoolValueOrDefault(false),
      steps = steps.Select((step, index) => new { t = Describe(step), s = index < at ? "done" : index == at ? "current" : "next" }).ToList()
    };
  }

  internal static string Describe(Value step)
  {
    try
    {
      var count = step["count"].IntValueOrDefault(0);
      switch (step["kind"].IntValueOrDefault(0))
      {
        case 0:
          var waypoint = OrderWaypoint.FromPropertyValue(step["wpt"]);
          if (waypoint is { WantsCouple: true } couple) return count > 0 ? $"Couple to {CarName(couple.CoupleToCarId)}, take {(count == 1 ? "1 car" : $"{count} cars")}" : $"Couple to {CarName(couple.CoupleToCarId)}";
          var smart = !step["smart"].IsNull && step["smart"].BoolValueOrDefault(false) ? ", smart entrance" : "";
          return $"Go to {(waypoint.HasValue ? WaypointName(waypoint.Value) : "waypoint")}{smart}";
        case 1:
          var side = step["side"].IntValueOrDefault(0);
          var where = side == 1 ? "front" : side == 2 ? "rear" : "last";
          if (step["keep"].BoolValueOrDefault(false)) return $"Keep {(count == 1 ? "1 car" : $"{count} cars")}{(side == 1 ? " in front" : side == 2 ? " behind" : "")}, leave the rest";
          if (count == AllCars) return side == 0 ? "Leave all cars" : $"Leave all {where} cars";
          return count == 1 ? $"Leave the {where} car" : $"Leave the {where} {count} cars";
        case 2:
          return "Wait until loaded";
        case 3:
          return "Wait until unloaded";
        case 4:
          if (count == 0) return "Wait until tomorrow";
          var hour = count < 0 ? StateManager.Shared?.Storage?.InterchangeServeHour ?? 6 : count;
          return $"Wait until {new GameDateTime(0, hour).TimeString()}";
        case 11:
          return "Wait until repaired";
        case 12:
          return $"Wait {count} minutes";
        case 14:
          return $"Wait until {count / 60}:{count % 60:D2}";
        case 5:
          return count == 1 ? "Wait 1 hour" : $"Wait {count} hours";
        case 6:
          var stationId = step["station"].StringValueOrDefault(null);
          var stop = OpsController.Shared?.AllPassengerStops?.FirstOrDefault(candidate => candidate != null && candidate.identifier == stationId);
          return $"Go to {stop?.DisplayName ?? "station"}";
        case 7:
          var ids = (step["fuel"].StringValueOrDefault("") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
          var stops = ids.Select(WebPlaces.FuelInfo).Where(info => info != null).ToList();
          var loads = string.Join(" and ", stops.Select(info => info.Value.Kind).Distinct());
          var town = stops.Select(info => info.Value.Town).FirstOrDefault(name => !string.IsNullOrEmpty(name));
          var below = count >= AlwaysRefuel ? "" : $" when below {count}%";
          return $"Refuel {(loads.Length > 0 ? loads : "fuel")}{(town != null ? $" at {town}" : "")}{below}";
        case 10:
          return "Couple back to the cars it left";
        case 13:
          return step["side"].IntValueOrDefault(1) == 2 ? "Run around the cars behind" : "Run around the cars in front";
        case 15:
          return "Couple to the cars left by an earlier order";
        case 9:
          return count == AllCars ? "Pick up all cars" : count == 1 ? "Pick up 1 car" : $"Pick up {count} cars";
        case 8:
          var turntable = TurntableName(step["turntable"].StringValueOrDefault(null));
          return step["exit"].IsNull ? $"Turn around at the {turntable}" : $"Use the {turntable}";
        default:
          return "Unknown order";
      }
    }
    catch (Exception)
    {
      return "Unknown order";
    }
  }

  internal static Turntable FindTurntable(string id)
  {
    return string.IsNullOrEmpty(id) ? null : Graph.Shared?.Turntables.FirstOrDefault(candidate => candidate != null && candidate.id == id);
  }

  private static string TurntableName(string id)
  {
    var turntable = FindTurntable(id);
    var town = turntable != null ? OpsController.Shared?.ClosestAreaForGamePosition(turntable.Position)?.name : null;
    return string.IsNullOrEmpty(town) ? "turntable" : $"{town} turntable";
  }

  private static string CarName(string id)
  {
    return !string.IsNullOrEmpty(id) && TrainController.Shared.TryGetCarForId(id, out var car) ? car.DisplayName : "a car";
  }

  internal static string WaypointName(OrderWaypoint waypoint)
  {
    var key = waypoint.LocationString;
    if (string.IsNullOrEmpty(key)) return "waypoint";
    if (PlaceNames.TryGetValue(key, out var name)) return name;
    name = FindPlace(key) ?? "waypoint";
    PlaceNames[key] = name;
    return name;
  }

  private static string FindPlace(string locationString)
  {
    var ops = OpsController.Shared;
    var graph = Graph.Shared;
    if (ops == null || graph == null) return null;
    Location location;
    try
    {
      location = graph.ResolveLocationString(locationString);
    }
    catch (Exception)
    {
      return null;
    }
    var nearby = new[]
    {
      location,
      graph.Current.LocationByMoving(location, NearbyMeters, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true),
      graph.Current.LocationByMoving(location, -NearbyMeters, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true)
    };
    foreach (var candidate in nearby)
    {
      foreach (var town in Places.Towns())
      {
        foreach (var place in town.Places)
        {
          if (place.Spans.Any(span => Contains(span, candidate))) return place.Name;
        }
      }
    }
    var area = ops.ClosestAreaForGamePosition(graph.GetPosition(location));
    return area != null && !string.IsNullOrEmpty(area.name) ? $"{area.name} waypoint" : null;
  }

  private static bool Contains(TrackSpan span, Location location)
  {
    try
    {
      return span.Contains(location);
    }
    catch (Exception)
    {
      return false;
    }
  }

  private static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

  private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
}
