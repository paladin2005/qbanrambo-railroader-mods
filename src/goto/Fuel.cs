using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Builtins;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Helpers;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Model.Definition.Components;
using Model.Ops;
using Model.Ops.Definition;
using RollingStock;
using Track;
using Track.Search;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal sealed class FuelStop
{
  public FuelStop(CarLoadTargetLoader loader, CarLoaderSequencer sequencer, Load load, string id, string name, string town, Vector3 point, Location location)
  {
    Loader = loader;
    Sequencer = sequencer;
    Load = load;
    Id = id;
    Name = name;
    Town = town;
    Point = point;
    Location = location;
  }

  public CarLoadTargetLoader Loader { get; }
  public CarLoaderSequencer Sequencer { get; }
  public Load Load { get; }
  public string Id { get; }
  public string Name { get; }
  public string Town { get; }
  public Vector3 Point { get; }
  public Location Location { get; }
  public string LoadName => Fuel.LoadName(Load);
}

internal sealed class FuelSlot
{
  public FuelSlot(Car car, int index, CarLoadTarget target)
  {
    Car = car;
    Index = index;
    Target = target;
  }

  public Car Car { get; }
  public int Index { get; }
  public CarLoadTarget Target { get; }
}

internal readonly struct FuelPlan
{
  public FuelPlan(OrderWaypoint waypoint, float routeDistance, float travel)
  {
    Waypoint = waypoint;
    RouteDistance = routeDistance;
    Travel = travel;
  }

  public OrderWaypoint Waypoint { get; }
  public float RouteDistance { get; }
  public float Travel { get; }
}

internal static class Fuel
{
  internal const string RefuelKey = "qbanrambo.goto.refuel";
  internal const string AlreadyPositioned = "Already positioned";

  private const float SnapRadius = 10f;
  private const float FullShare = 0.999f;

  private static readonly MethodInfo FuelCarMethod = AccessTools.Method(typeof(SteamLocomotive), "FuelCar");
  private static readonly FieldInfo SourceIndustryField = AccessTools.Field(typeof(CarLoadTargetLoader), "sourceIndustryId");
  private static readonly FieldInfo TooltipTitleField = AccessTools.Field(typeof(IndustryContentHoverable), "tooltipTitle");

  internal static Car FuelCar(BaseLocomotive locomotive)
  {
    if (locomotive is SteamLocomotive && FuelCarMethod != null) return FuelCarMethod.Invoke(locomotive, null) as Car;
    return locomotive;
  }

  internal static List<(FuelSlot Slot, Load Load)> Slots(Car car)
  {
    var result = new List<(FuelSlot, Load)>();
    var slots = car?.Definition?.LoadSlots;
    if (slots == null) return result;
    var targets = car.GetComponentsInChildren<CarLoadTarget>();
    for (var index = 0; index < slots.Count; index++)
    {
      var load = CarLoadLibrary.GetLoad(slots[index].RequiredLoadIdentifier);
      var target = targets.FirstOrDefault(candidate => candidate.slotIndex == index);
      if (load != null && target != null) result.Add((new FuelSlot(car, index, target), load));
    }
    return result;
  }

  internal static FuelSlot Slot(Car car, Load load)
  {
    return load == null ? null : Slots(car).Where(entry => entry.Load.id == load.id).Select(entry => entry.Slot).FirstOrDefault();
  }

  internal static float Level(FuelSlot slot, Load load)
  {
    var capacity = slot.Car.Definition.LoadSlots[slot.Index].CalculateMaximumCapacity(load.id, slot.Car);
    if (capacity <= 0f) return 1f;
    return Mathf.Clamp01((slot.Car.GetLoadInfo(slot.Index)?.Quantity ?? 0f) / capacity);
  }

  internal static bool IsFull(FuelSlot slot, Load load) => Level(slot, load) > FullShare;

  internal static string LoadName(Load load) => string.IsNullOrWhiteSpace(load?.description) ? load?.id : load.description;

  internal static List<FuelStop> Stops()
  {
    var stops = new List<FuelStop>();
    var graph = Graph.Shared;
    if (graph == null) return stops;
    foreach (var loader in UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(FindObjectsSortMode.None))
    {
      try
      {
        var stop = StopFor(loader, graph);
        if (stop != null) stops.Add(stop);
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Warning($"Skipped a fuel loader in Go to: {exception.Message}");
      }
    }
    return stops;
  }

  private static FuelStop StopFor(CarLoadTargetLoader loader, Graph graph)
  {
    if (loader == null || !loader.isActiveAndEnabled) return null;
    var load = loader.load;
    var id = loader.keyValueObject != null ? loader.keyValueObject.RegisteredId : null;
    if (load == null || string.IsNullOrEmpty(id)) return null;
    var root = loader.GetComponentInParent<CarLoaderBuiltin>();
    var owner = root != null ? root.gameObject : loader.gameObject;
    var sequencer = owner.GetComponentInChildren<CarLoaderSequencer>(true);
    var point = WorldTransformer.WorldToGame(loader.transform.position);
    if (!graph.TryGetLocationFromGamePoint(point, SnapRadius, out var location) || !DeadEnds.IsUsable(location.segment)) return null;
    var town = OpsController.Shared?.ClosestAreaForGamePosition(point)?.name;
    return new FuelStop(loader, sequencer, load, id, NameFor(owner, load), town, point, location);
  }

  private static string NameFor(GameObject owner, Load load)
  {
    var hoverable = owner.GetComponentInChildren<IndustryContentHoverable>(true);
    var title = hoverable != null ? TooltipTitleField?.GetValue(hoverable) as string : null;
    return string.IsNullOrWhiteSpace(title) ? LoadName(load) : title;
  }

  internal static bool InStock(FuelStop stop)
  {
    var industryId = SourceIndustryField?.GetValue(stop.Loader) as string;
    if (string.IsNullOrEmpty(industryId)) return true;
    var industry = OpsController.Shared?.IndustryForId(industryId);
    return industry != null && industry.Storage.QuantityInStorage(stop.Load) > 0f;
  }

  internal static void Request(FuelStop stop, bool on)
  {
    var sequencer = stop.Sequencer;
    var sequencerId = sequencer != null && sequencer.keyValueObject != null ? sequencer.keyValueObject.RegisteredId : null;
    if (!string.IsNullOrEmpty(sequencerId)) StateManager.ApplyLocal(new PropertyChange(sequencerId, sequencer.readWantsLoadingKey, new BoolPropertyValue(on)));
    else StateManager.ApplyLocal(new PropertyChange(stop.Id, stop.Loader.canLoadBoolKey, new BoolPropertyValue(on)));
  }

  internal static void Hatch(FuelSlot slot, Load load, bool open)
  {
    if (slot == null || load == null || !load.id.Contains("water")) return;
    var hatch = slot.Car.Definition?.ComponentsOfType<ToggleAnimationComponent>()
      .FirstOrDefault(component => $"{component.Key} {component.Name} {component.Title}".ToLowerInvariant() is var text && text.Contains("water") && text.Contains("hatch"));
    var values = slot.Car.KeyValueObject;
    if (string.IsNullOrEmpty(hatch?.Key) || values == null || values[hatch.Key].BoolValue == open) return;
    StateManager.ApplyLocal(new PropertyChange(values.RegisteredId, hatch.Key, new BoolPropertyValue(open)));
  }

  internal static Vector3 TargetPoint(FuelSlot slot)
  {
    var car = slot.Car;
    var local = car.transform.InverseTransformPoint(slot.Target.transform.position);
    return car.GetTransformMatrix(Graph.Shared.Current).MultiplyPoint3x4(local);
  }

  internal static float Offset(FuelSlot slot, FuelStop stop) => Vector3.Distance(TargetPoint(slot).ZeroY(), stop.Point.ZeroY());

  internal static float Along(FuelSlot slot, FuelStop stop) => Vector3.Dot((TargetPoint(slot) - stop.Point).ZeroY(), TrackDirection(stop));

  internal static float Across(FuelSlot slot, FuelStop stop)
  {
    var along = Along(slot, stop);
    var offset = Offset(slot, stop);
    return Mathf.Sqrt(Mathf.Max(0f, offset * offset - along * along));
  }

  internal static float Tolerance(FuelSlot slot, FuelStop stop) => slot.Target.radius + stop.Loader.radius;

  internal static bool LinedUp(FuelSlot slot, FuelStop stop) => Mathf.Abs(Along(slot, stop)) <= Tolerance(slot, stop);

  internal static Vector3 TrackDirection(FuelStop stop) => Graph.Shared.GetPositionDirection(stop.Location).Direction.ZeroY().normalized;

  internal static FuelPlan? Plan(BaseLocomotive locomotive, FuelSlot slot, FuelStop stop, float lead, out string message)
  {
    message = null;
    var graph = Graph.Shared;
    var view = graph.Current;
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var index = cars.IndexOf(slot.Car);
    if (index < 0)
    {
      message = $"{slot.Car.DisplayName} isn't coupled to {locomotive.DisplayName}";
      return null;
    }
    var point = TargetPoint(slot).ZeroY();
    var endA = graph.GetPosition(slot.Car.LocationA).ZeroY();
    var endB = graph.GetPosition(slot.Car.LocationB).ZeroY();
    var axis = endB - endA;
    var length = axis.magnitude;
    var inside = length > 0.01f ? Vector3.Dot(point - endA, axis / length) : slot.Car.carLength / 2f;
    var fromA = inside;
    for (var k = 0; k < index; k++) fromA += cars[k].carLength + Gap(graph, cars[k], cars[k + 1]);
    var fromB = length - inside;
    for (var k = index + 1; k < cars.Count; k++) fromB += cars[k].carLength + Gap(graph, cars[k - 1], cars[k]);
    var ends = index <= cars.Count - 1 - index
      ? new[] { (End: WaypointEnd.A, Offset: fromA), (End: WaypointEnd.B, Offset: fromB) }
      : new[] { (End: WaypointEnd.B, Offset: fromB), (End: WaypointEnd.A, Offset: fromA) };
    foreach (var end in ends)
    {
      PlacementPlan? best = null;
      var bestMark = default(Location);
      foreach (var sign in new[] { 1f, -1f })
      {
        Location mark;
        try
        {
          mark = graph.LocationOrientedToward(view.LocationByMoving(stop.Location, sign * end.Offset, checkSwitchAgainstMovement: false, stopAtEndOfTrack: false), stop.Location);
        }
        catch (Exception)
        {
          continue;
        }
        var plan = Placement(locomotive, view, mark, end.End);
        if (plan.IsPlanned && plan.Waypoint.HasValue)
        {
          if (best.HasValue && plan.RouteDistance >= best.Value.RouteDistance) continue;
          best = plan;
          bestMark = mark;
        }
        else if (plan.Message?.Contains(AlreadyPositioned) ?? false)
        {
          message = plan.Message;
          return null;
        }
        else
        {
          message ??= plan.Message;
        }
      }
      if (!best.HasValue) continue;
      var leads = Leads(best.Value, end.End);
      var toward = Vector3.Dot((stop.Point - graph.GetPosition(bestMark)).ZeroY(), TrackDirection(stop)) >= 0f ? 1f : -1f;
      var travel = leads ? -toward : toward;
      if (Mathf.Abs(lead) > 0.01f && (leads || lead < end.Offset))
      {
        try
        {
          var shifted = Placement(locomotive, view, view.LocationByMoving(bestMark, leads ? -lead : lead, checkSwitchAgainstMovement: false, stopAtEndOfTrack: false), end.End);
          if (shifted.IsPlanned && shifted.Waypoint.HasValue && Leads(shifted, end.End) == leads) best = shifted;
        }
        catch (Exception)
        {
        }
      }
      message = null;
      return new FuelPlan(best.Value.Waypoint.Value, best.Value.RouteDistance, travel);
    }
    return null;
  }

  private static PlacementPlan Placement(BaseLocomotive locomotive, IGraphView view, Location mark, WaypointEnd end)
  {
    return PosedWaypoint.Plan(locomotive, view, mark, null, end, WaypointPose.ToBodyToward(mark.end.Flipped()));
  }

  private static bool Leads(PlacementPlan plan, WaypointEnd end)
  {
    var route = plan.Route;
    return route == null || route.Count == 0 || route[route.Count - 1].LeadingEnd == PosedWaypoint.ToConsistEnd(end);
  }

  internal static OrderWaypoint? BackOff(BaseLocomotive locomotive, FuelStop stop, float distance, float travel)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    if (cars.Count == 0) return null;
    var graph = Graph.Shared;
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var forward = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Forward;
    var direction = TrackDirection(stop);
    var ends = new[] { cars[0].LocationA, cars[cars.Count - 1].LocationB }
      .OrderBy(end => Vector3.Dot(graph.GetPositionDirection(end).Direction.ZeroY(), direction) * travel);
    foreach (var end in ends)
    {
      Location target;
      try
      {
        target = graph.Current.LocationByMoving(end, distance, checkSwitchAgainstMovement: false, stopAtEndOfTrack: false);
      }
      catch (Exception)
      {
        continue;
      }
      var waypoint = new OrderWaypoint(Graph.LocationToString(target), null);
      if (CanRoute(locomotive, cars, length, forward, waypoint) || CanRoute(locomotive, cars, length, !forward, waypoint)) return waypoint;
    }
    return null;
  }

  private static bool CanRoute(BaseLocomotive locomotive, List<Car> cars, float length, bool forward, OrderWaypoint waypoint)
  {
    return AutoEngineerPlanner.TryPlanRoute(locomotive, cars, length, forward, waypoint, Graph.Shared, out _, out _, out _, out _);
  }

  private static float Gap(Graph graph, Car first, Car second)
  {
    return Vector3.Distance(graph.GetPosition(first.LocationB).ZeroY(), graph.GetPosition(second.LocationA).ZeroY());
  }
}
