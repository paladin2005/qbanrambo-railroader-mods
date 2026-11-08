using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using Model.Ops;
using Railroader.Modding;
using Track;
using UI;
using UI.Common;
using UI.EngineControls;

namespace QbanRambo.Goto;

internal static class GotoMenu
{
  internal const string MenuHookKey = "qbanrambo.goto.menu";
  private const string FutureCarsKey = "qbanrambo.train-orders.future-cars";
  internal const string CapturedKey = "qbanrambo.train-orders.captured";

  internal static IModLogger Log;
  internal static Car PendingCar;

  internal static readonly Func<object, IReadOnlyList<MenuItem>> MenuHook = target => target is BaseLocomotive locomotive ? Safe(() => OrdersTownItems(locomotive)) : Array.Empty<MenuItem>();

  internal static IReadOnlyList<MenuItem> WithGotoItem(IReadOnlyList<MenuItem> items, BaseLocomotive locomotive)
  {
    var list = items.ToList();
    var select = list.FindIndex(item => item.Label == "Select" || item.Label == "Deselect");
    list.Insert(select < 0 ? list.Count : select + 1, MenuItem.Submenu("Go to", () => Safe(() => GotoItems(locomotive))));
    return list;
  }

  internal static IReadOnlyList<MenuItem> Safe(Func<IReadOnlyList<MenuItem>> build)
  {
    try
    {
      return build();
    }
    catch (Exception exception)
    {
      Log?.Error(exception, "Couldn't build the Go to menu");
      return new[] { MenuItem.Action("Unavailable, see the Message Log", null, disabled: true) };
    }
  }

  internal static IReadOnlyList<MenuItem> GotoItems(BaseLocomotive locomotive)
  {
    var waybills = WaybillDestinations(locomotive);
    var towns = Towns();
    return new[]
    {
      waybills.Count == 0
        ? MenuItem.Submenu("Waybills", Array.Empty<MenuItem>(), "None in this train", disabled: true)
        : MenuItem.Submenu("Waybills", () => Safe(() => WaybillItems(locomotive, waybills))),
      towns.Count == 0
        ? MenuItem.Submenu("Towns", Array.Empty<MenuItem>(), "None available yet", disabled: true)
        : MenuItem.Submenu("Towns", () => Safe(() => TownItems(locomotive, towns))),
      RefuelMenu.Item(locomotive),
      TurntableMenu.Item(locomotive),
      RunAroundMenu.Item(locomotive),
      MenuItem.Action("Release handbrakes", ToggleReleaseHandbrakes, check: GotoSettings.ReleaseHandbrakes ? DropdownMenu.CheckState.Checked : DropdownMenu.CheckState.Unchecked)
    };
  }

  private static void ToggleReleaseHandbrakes()
  {
    try
    {
      GotoSettings.ReleaseHandbrakes = !GotoSettings.ReleaseHandbrakes;
      Toast.Present($"Release handbrakes on Go to: {(GotoSettings.ReleaseHandbrakes ? "On" : "Off")}", ToastPosition.Bottom);
    }
    catch (Exception exception)
    {
      Log?.Error(exception, "Couldn't save the Go to settings");
    }
  }

  private static IReadOnlyList<MenuItem> WaybillItems(BaseLocomotive locomotive, List<Place> destinations)
  {
    return destinations
      .Select(destination => destination.Position.HasLocation
        ? MenuItem.Submenu(destination.Label, () => Safe(() => DeliverItems(locomotive, destination.Position)), TownName(destination.Position))
        : PlaceItem(locomotive, destination.Label, destination.Position, TownName(destination.Position), true))
      .ToList();
  }

  private static IReadOnlyList<MenuItem> DeliverItems(BaseLocomotive locomotive, OpsCarPosition position)
  {
    var count = Delivery.Count(locomotive, position);
    var ready = Delivery.Block(locomotive, position, out _) != null;
    var items = new List<MenuItem>
    {
      MenuItem.Action($"Deliver {Delivery.Cars(count)}", () => Delivery.Send(locomotive, position), ready ? "Leave them on the track, the engine goes free" : "Not together at one end of the train", !ready)
    };
    var standing = Coupling.StandingCars(locomotive, position).Count;
    var smart = DeadEnds.HasCars(locomotive) && DeadEnds.IsDeadEnd(position);
    if (standing == 0 && !smart) items.Add(MenuItem.Action("Just go", () => Send(locomotive, position, null), "Stop there with the whole train"));
    else items.AddRange(EntranceItems(locomotive, position, standing, smart, smart));
    return items;
  }

  private static IReadOnlyList<MenuItem> TownItems(BaseLocomotive locomotive, List<Town> towns)
  {
    return towns
      .Select(town => MenuItem.Submenu(town.Name, () => Safe(() => town.Places.Select(place => PlaceItem(locomotive, place.Label, place.Position)).ToList())))
      .ToList();
  }

  private static IReadOnlyList<MenuItem> OrdersTownItems(BaseLocomotive locomotive)
  {
    var towns = Towns();
    if (towns.Count == 0) return new[] { MenuItem.Action("No towns available yet", null, disabled: true) };
    return towns
      .Select(town => MenuItem.Submenu(town.Name, () => Safe(() => town.Places
        .Select(place => Stations.Find(place.Position) != null
          ? MenuItem.Action(place.Label, () => Send(locomotive, place.Position, null), "Passenger station")
          : PlaceItem(locomotive, place.Label, place.Position, later: true))
        .ToList())))
      .ToList();
  }

  private static MenuItem PlaceItem(BaseLocomotive locomotive, string label, OpsCarPosition position, string subtitle = null, bool disabled = false, bool later = false)
  {
    if (disabled) return MenuItem.Action(label, () => Send(locomotive, position, null), subtitle, disabled);
    var standing = Coupling.StandingCars(locomotive, position).Count;
    var deadEnd = DeadEnds.IsDeadEnd(position);
    var coupling = DeadEnds.HasCars(locomotive) && deadEnd;
    var smart = deadEnd && (DeadEnds.HasCars(locomotive) || (later && HasCarsLater(locomotive)));
    if (standing == 0 && !smart) return MenuItem.Action(label, () => Send(locomotive, position, null), subtitle);
    return MenuItem.Submenu(label, () => Safe(() => EntranceItems(locomotive, position, standing, smart, coupling)), subtitle);
  }

  private static bool HasCarsLater(BaseLocomotive locomotive)
  {
    try
    {
      return AppDomain.CurrentDomain.GetData(FutureCarsKey) is Func<object, int> future && future(locomotive) > 0;
    }
    catch (Exception exception)
    {
      Log?.Warning($"Couldn't ask Train Orders about the train after its orders: {exception.Message}");
      return false;
    }
  }

  private static IReadOnlyList<MenuItem> EntranceItems(BaseLocomotive locomotive, OpsCarPosition position, int standing, bool smart, bool coupling)
  {
    var items = new List<MenuItem>();
    if (standing > 0) items.Add(MenuItem.Action("Couple to existing cars", () => SendCouple(locomotive, position, false), standing == 1 ? "1 car on this track" : $"{standing} cars on this track"));
    if (standing > 0 && coupling) items.Add(MenuItem.Action("Smart coupling", () => SendCouple(locomotive, position, true), "Your cars couple to them, the engine stays at the open end"));
    if (smart)
    {
      items.Add(MenuItem.Action("Smart entrance", () => Send(locomotive, position, DeadEnds.SmartEntrance), "Push the cars in, the engine stays at the open end"));
      items.Add(MenuItem.Action("Normal entrance", () => Send(locomotive, position, DeadEnds.NormalEntrance), "Go in the way the train is facing"));
    }
    else
    {
      items.Add(MenuItem.Action("Don't couple", () => Send(locomotive, position, null), "Stop short of the cars"));
    }
    return items;
  }

  private static void SendCouple(BaseLocomotive locomotive, OpsCarPosition position, bool smart)
  {
    try
    {
      var standing = Coupling.StandingCars(locomotive, position);
      if (standing.Count == 0)
      {
        Toast.Present($"No cars on {position.DisplayName} anymore", ToastPosition.Bottom);
        return;
      }
      var order = Coupling.Plan(locomotive, standing, smart ? DeadEnds.CarsEnd(locomotive) : null, out var car, out var message);
      if (!order.HasValue)
      {
        var how = smart ? " with its cars first" : "";
        Toast.Present(message != null ? $"{locomotive.DisplayName} can't couple{how}: {message}" : $"{locomotive.DisplayName} can't reach the cars on {position.DisplayName}{how}", ToastPosition.Bottom);
        return;
      }
      var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
      AppDomain.CurrentDomain.SetData(CapturedKey, null);
      AppDomain.CurrentDomain.SetData(CoupleWatch.WatchedKey, true);
      try
      {
        helper.SetWaypointOrder(order.Value);
        if (AppDomain.CurrentDomain.GetData(CapturedKey) is true) return;
      }
      finally
      {
        AppDomain.CurrentDomain.SetData(CapturedKey, null);
        AppDomain.CurrentDomain.SetData(CoupleWatch.WatchedKey, null);
      }
      CoupleWatch.Begin(locomotive, car, order.Value.LocationString);
      var released = GotoSettings.ReleaseHandbrakes ? ReleaseHandbrakes(locomotive) : 0;
      var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
      Toast.Present($"{locomotive.DisplayName} heading to couple to {car.DisplayName} on {position.DisplayName}{handbrakes}", ToastPosition.Bottom);
    }
    catch (Exception exception)
    {
      Log?.Error(exception, $"Coupling at {position.Identifier} failed");
      Toast.Present("Go to failed. See the Message Log.", ToastPosition.Bottom);
    }
  }

  private static List<Place> WaybillDestinations(BaseLocomotive locomotive)
  {
    List<Place> towns = null;
    return locomotive.EnumerateCoupled()
      .Where(car => car.Waybill.HasValue && !car.Waybill.Value.Completed)
      .GroupBy(car => Delivery.KeyFor(car.Waybill.Value.Destination))
      .Select(group =>
      {
        var position = group.First().Waybill.Value.Destination;
        var cars = group.Count();
        var names = group.Select(car => car.Waybill.Value.Destination.DisplayName).Distinct().ToList();
        var name = names.Count == 1 ? names[0] : TrackName(group.Key, ref towns) ?? names[0];
        if (names.Count > 1 && position.Spans != null && position.Spans.Length > 0) position = new OpsCarPosition(name, position.Identifier, position.Spans);
        return new Place($"{name} ({cars} {(cars == 1 ? "car" : "cars")})", position, cars);
      })
      .OrderByDescending(place => place.Cars)
      .ThenBy(place => place.Label)
      .ToList();
  }

  private static string TrackName(string key, ref List<Place> places)
  {
    places ??= Towns().SelectMany(town => town.Places).ToList();
    return places.FirstOrDefault(place => Delivery.KeyFor(place.Position) == key)?.Label;
  }

  private static List<Town> Towns()
  {
    var areas = OpsController.Shared?.Areas ?? Enumerable.Empty<Area>();
    return areas
      .Select(area => new Town(area.name, Places(area)))
      .Where(town => town.Places.Count > 0)
      .OrderBy(town => town.Name)
      .ToList();
  }

  private static List<Place> Places(Area area)
  {
    var places = new List<Place>();
    foreach (var industry in area.Industries.Where(industry => industry.IsEnabledReachable))
    {
      var tracks = industry.TrackDisplayables
        .Where(IsAvailable)
        .Select(track => (Track: track, Spans: (track.TrackSpans ?? Enumerable.Empty<TrackSpan>()).Where(span => span != null).ToArray()))
        .Where(entry => entry.Spans.Length > 0)
        .GroupBy(entry => string.Join("|", entry.Spans.Select(SpanKey).OrderBy(key => key)));
      foreach (var group in tracks)
      {
        var names = group.Select(entry => entry.Track.DisplayName).Distinct().ToList();
        var label = names.Count == 1 ? names[0] : industry.DisplayName;
        var first = group.First();
        places.Add(new Place(label, new OpsCarPosition(label, Identifier(first.Track, label), first.Spans), 0));
      }
    }
    return places.OrderBy(place => place.Label).ToList();
  }

  private static bool IsAvailable(IIndustryTrackDisplayable track)
  {
    try
    {
      return track switch
      {
        Interchange interchange => interchange.IsVisible && interchange.IsAnySpanReachable && !interchange.Disabled,
        IndustryComponent component => component.IsVisible && component.IsAnySpanReachable,
        PassengerStop stop => stop.IsEnabledReachable,
        _ => false
      };
    }
    catch (Exception exception)
    {
      Log?.Warning($"Skipped {track?.DisplayName} in Go to: {exception.Message}");
      return false;
    }
  }

  private static string Identifier(IIndustryTrackDisplayable track, string fallback)
  {
    return track switch
    {
      IndustryComponent component => component.Identifier,
      PassengerStop stop => stop.identifier,
      _ => fallback
    };
  }

  internal static string SpanKey(TrackSpan span)
  {
    return string.IsNullOrEmpty(span.id) ? span.GetInstanceID().ToString() : span.id;
  }

  private static string TownName(OpsCarPosition position)
  {
    try
    {
      return OpsController.Shared?.AreaForCarPosition(position)?.name;
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static void Send(BaseLocomotive locomotive, OpsCarPosition position, string deadEndChoice)
  {
    try
    {
      var target = PickTarget(locomotive, position, out var failure);
      if (target == null)
      {
        Toast.Present(failure ?? $"{locomotive.DisplayName} can't find a route to {position.DisplayName}.", ToastPosition.Bottom);
        return;
      }
      var orders = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
      string note = null;
      var stop = deadEndChoice == DeadEnds.SmartEntrance ? null : Stations.Find(position);
      var coaches = stop != null && Stations.Coaches(locomotive).Count > 0;
      var stopsOff = coaches && !Stations.StopsEnabled;
      if (coaches && !stopsOff) target = Stations.Beyond(locomotive, stop, target.Value) ?? target;
      var waypoint = deadEndChoice == DeadEnds.SmartEntrance ? DeadEnds.Waypoint(locomotive, target.Value, out note) : new OrderWaypoint(Graph.LocationToString(target.Value), null);
      AppDomain.CurrentDomain.SetData(DeadEnds.ChoiceKey, deadEndChoice);
      AppDomain.CurrentDomain.SetData(Stations.StationKey, stop?.identifier);
      AppDomain.CurrentDomain.SetData(CapturedKey, null);
      var captured = false;
      try
      {
        orders.SetWaypointOrder(waypoint);
        captured = (stop != null && AppDomain.CurrentDomain.GetData(Stations.StationKey) == null) || AppDomain.CurrentDomain.GetData(CapturedKey) is true;
      }
      finally
      {
        AppDomain.CurrentDomain.SetData(DeadEnds.ChoiceKey, null);
        AppDomain.CurrentDomain.SetData(Stations.StationKey, null);
        AppDomain.CurrentDomain.SetData(CapturedKey, null);
      }
      if (captured) return;
      if (coaches && !captured)
      {
        Stations.Tick(locomotive, stop);
        StationWatch.Begin(locomotive, stop, waypoint.LocationString);
        note = stopsOff ? "Passenger Stops is off in Company > Settings, so the coaches may not line up with the platform" : "stopping with the coaches at the platform";
      }
      var released = GotoSettings.ReleaseHandbrakes ? ReleaseHandbrakes(locomotive) : 0;
      var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
      var how = note == null ? "" : $", {note}";
      Toast.Present($"{locomotive.DisplayName} heading to {position.DisplayName}{how}{handbrakes}", ToastPosition.Bottom);
    }
    catch (Exception exception)
    {
      Log?.Error(exception, $"Go to {position.Identifier} failed");
      Toast.Present("Go to failed. See the Message Log.", ToastPosition.Bottom);
    }
  }

  internal static int ReleaseHandbrakes(BaseLocomotive locomotive)
  {
    var released = 0;
    foreach (var car in locomotive.EnumerateCoupled())
    {
      if (car.air == null || !car.air.handbrakeApplied) continue;
      car.SetHandbrake(false);
      released++;
    }
    return released;
  }

  internal static Location? PickTarget(BaseLocomotive locomotive, OpsCarPosition position, out string failure)
  {
    failure = null;
    var cars = locomotive.EnumerateCoupled().ToList();
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var forward = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Forward;
    Location? best = null;
    var bestNear = float.MaxValue;
    foreach (var span in position.Spans ?? Array.Empty<TrackSpan>())
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
      best = reachable.OrderByDescending(candidate => candidate.Distance).First().Location;
    }
    return best;
  }

  private static float? RouteDistance(BaseLocomotive locomotive, List<Car> cars, float length, bool forward, Location location, ref string failure)
  {
    var waypoint = new OrderWaypoint(Graph.LocationToString(location), null);
    if (AutoEngineerPlanner.TryPlanRoute(locomotive, cars, length, forward, waypoint, Graph.Shared, out _, out _, out var message, out var distance)) return distance;
    failure ??= message;
    return null;
  }

  private sealed class Place
  {
    public Place(string label, OpsCarPosition position, int cars)
    {
      Label = label;
      Position = position;
      Cars = cars;
    }

    public string Label { get; }
    public OpsCarPosition Position { get; }
    public int Cars { get; }
  }

  private sealed class Town
  {
    public Town(string name, List<Place> places)
    {
      Name = name;
      Places = places;
    }

    public string Name { get; }
    public List<Place> Places { get; }
  }
}
