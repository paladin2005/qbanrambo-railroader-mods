using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Model;
using UI;
using UI.Common;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class OrderActions
{
  internal const int MaxCut = 10;

  private static readonly int[] WaitHourOptions = { 1, 2, 4, 6, 8, 10, 12 };

  private static readonly int[] WaitMinuteOptions = { 5, 10, 15, 30, 45 };

  internal static readonly int[] BelowOptions = { 25, 50, 75, Fuel.Always };

  private static List<OrderStep> _copied = new();
  private static bool _copiedRepeat;
  private static bool _copiedPassSignals;
  private static string _copiedFrom;

  internal static string Status(OrderList orders)
  {
    if (orders.Running && !orders.Finished) return $"Running, order {orders.Current + 1} of {orders.Steps.Count}";
    if (orders.Status != null) return orders.Status;
    return orders.Steps.Count == 0 ? "No orders" : "Stopped";
  }

  internal static string Describe(OrderStep step)
  {
    return step.Kind switch
    {
      StepKind.Move when step.Waypoint is { WantsCouple: true } waypoint => $"Couple to {CarName(waypoint.CoupleToCarId)}",
      StepKind.Move => $"Go to {Places.NameFor(step.Waypoint?.LocationString) ?? "waypoint"}",
      StepKind.Cut => DescribeCut(step),
      StepKind.WaitLoad => "Wait until loaded",
      StepKind.WaitUnload => "Wait until unloaded",
      StepKind.WaitDay => step.Count == 0 ? "Wait until tomorrow" : $"Wait until {Clock(step.WaitHour)}",
      StepKind.WaitMinutes => $"Wait {step.Count} minutes",
      StepKind.WaitTime => $"Wait until {ClockMinute(step.Count)}",
      StepKind.WaitRepair => "Wait until repaired",
      StepKind.WaitHours => $"Wait {Hours(step.Count)}",
      StepKind.Station => $"Go to {Passengers.Name(step.StationId)}",
      StepKind.Refuel => Fuel.Describe(step.FuelIds),
      StepKind.Turntable => DescribeTurntable(step),
      StepKind.PickUp => step.Count == Maneuvers.AllCars ? "Pick up all cars" : step.Count == 1 ? "Pick up 1 car" : $"Pick up {step.Count} cars",
      StepKind.CoupleBack => "Couple back to the cars it left",
      StepKind.CoupleTo => "Couple to the cars left by an earlier order",
      StepKind.RunAround => step.Side == CutSide.Rear ? "Run around the cars behind" : "Run around the cars in front",
      _ => "Unknown order"
    };
  }

  internal static string RowText(OrderStep step, OrderList orders)
  {
    if (step.Kind == StepKind.CoupleTo)
    {
      var source = orders.IndexOfId(step.From);
      return source < 0 ? "Couple to cars left by a removed order" : $"Couple to the cars left by order {source + 1}";
    }
    if (step.Kind != StepKind.Turntable) return Describe(step);
    var name = Turntables.Find(step.TurntableId)?.Name;
    return string.IsNullOrEmpty(name) ? "Turntable" : char.ToUpperInvariant(name[0]) + name.Substring(1);
  }

  private static string DescribeTurntable(OrderStep step)
  {
    var site = Turntables.Find(step.TurntableId);
    var name = site?.Name ?? "turntable";
    if (!step.Exit.HasValue) return $"Turn around at the {name}";
    var track = site?.Tracks.FirstOrDefault(candidate => candidate.Index == step.Exit.Value);
    if (track == null) return $"Use the {name}";
    return track.Stall ? $"Park on {track.Label} at the {name}" : $"Leave the {name} on {track.Label}";
  }

  internal static IReadOnlyList<MenuItem> TurntableItems(BaseLocomotive locomotive)
  {
    var sites = Turntables.Sites();
    if (sites.Count == 0) return new[] { MenuItem.Action("No turntables available", () => { }, null, true) };
    var origin = locomotive.GetCenterPosition(Track.Graph.Shared.Current);
    return sites
      .OrderBy(site => Vector3.Distance(origin, site.Turntable.Position))
      .Select(site =>
      {
        var id = site.Turntable.id;
        var distance = Vector3.Distance(origin, site.Turntable.Position);
        return MenuItem.Action(site.Name, () => Run(() => AddTurntable(locomotive, id, null)), $"{Fuel.Distance(distance)}, turn around (change it in the order)");
      })
      .ToList();
  }

  internal static void AddTurntable(Car locomotive, string turntableId, int? exit)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.Turntable(turntableId, exit)));
  }

  internal static List<(int? Exit, string Label)> ExitOptions(TurntableSite site)
  {
    var options = new List<(int? Exit, string Label)> { (null, "Turn around") };
    if (site == null) return options;
    options.AddRange(site.Tracks.Where(track => track.Stall).Select(track => ((int?)track.Index, $"Park on {track.Label}")));
    options.AddRange(site.Tracks.Where(track => !track.Stall).Select(track => ((int?)track.Index, $"Leave on {track.Label}")));
    return options;
  }

  internal static List<(string Track, string Label)> TrackOptions(string stationId)
  {
    var options = new List<(string Track, string Label)> { (null, "Shortest route") };
    options.AddRange(StationTracks.Options(Passengers.Stop(stationId)));
    return options;
  }

  internal static void SetTrack(Car locomotive, int index, string track)
  {
    Edit(locomotive, orders =>
    {
      if (index >= 0 && index < orders.Steps.Count) orders.Steps[index] = orders.Steps[index].WithTrack(track);
    });
  }

  internal static void SetExit(Car locomotive, int index, int? exit)
  {
    Edit(locomotive, orders =>
    {
      if (index >= 0 && index < orders.Steps.Count) orders.Steps[index] = orders.Steps[index].WithExit(exit);
    });
  }

  internal static string DescribeCut(OrderStep step)
  {
    if (step.Keep)
    {
      var kept = step.Count == 1 ? "1 car" : $"{step.Count} cars";
      return step.Side switch
      {
        CutSide.Front => $"Keep {kept} in front, leave the rest",
        CutSide.Rear => $"Keep {kept} behind, leave the rest",
        _ => $"Keep {kept}, leave the rest"
      };
    }
    if (step.Count == Maneuvers.AllCars) return $"Leave {AllCarsLabel(step.Side).ToLowerInvariant()}";
    var cars = step.Count == 1 ? "car" : $"{step.Count} cars";
    return step.Side switch
    {
      CutSide.Front => $"Leave the front {cars}",
      CutSide.Rear => $"Leave the rear {cars}",
      _ => $"Leave the last {cars}"
    };
  }

  private static string AllCarsLabel(CutSide side) => side == CutSide.Auto ? "All cars" : $"All {side.ToString().ToLowerInvariant()} cars";

  internal static IReadOnlyList<MenuItem> CutItems(BaseLocomotive locomotive)
  {
    var (front, rear) = Forecast.CarsAroundLater(locomotive);
    var items = front == 0 || rear == 0
      ? SideItems(locomotive, CutSide.Auto).ToList()
      : new List<MenuItem>
      {
        MenuItem.Submenu("Front", () => SideItems(locomotive, CutSide.Front)),
        MenuItem.Submenu("Rear", () => SideItems(locomotive, CutSide.Rear))
      };
    if (front > 0) items.Add(MenuItem.Action("Run around the cars in front", () => Run(() => AddRunAround(locomotive, CutSide.Front)), "Leave them, go around them and couple to their far end"));
    if (rear > 0) items.Add(MenuItem.Action("Run around the cars behind", () => Run(() => AddRunAround(locomotive, CutSide.Rear)), "Leave them, go around them and couple to their far end"));
    return items;
  }

  internal static void AddRunAround(Car locomotive, CutSide side)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.RunAround(side)));
  }

  private static IReadOnlyList<MenuItem> SideItems(Car locomotive, CutSide side)
  {
    return CountItems(locomotive, side)
      .Append(MenuItem.Submenu("Keep", () => KeepItems(locomotive, side), "Keep the cars next to the engine, leave the rest"))
      .ToList();
  }

  private static IReadOnlyList<MenuItem> KeepItems(Car locomotive, CutSide side)
  {
    return Enumerable.Range(1, MaxCut)
      .Select(count => MenuItem.Action(count == 1 ? "Keep 1 car" : $"Keep {count} cars", () => Run(() => Edit(locomotive, orders => orders.Add(OrderStep.Cut(count, side, true))))))
      .ToList();
  }

  private static IReadOnlyList<MenuItem> CountItems(Car locomotive, CutSide side)
  {
    var prefix = side == CutSide.Auto ? "Last" : side.ToString();
    return Enumerable.Range(1, MaxCut)
      .Select(count => MenuItem.Action(count == 1 ? $"{prefix} car" : $"{prefix} {count} cars", () => Run(() => AddCut(locomotive, count, side))))
      .Append(MenuItem.Action(AllCarsLabel(side), () => Run(() => AddCut(locomotive, Maneuvers.AllCars, side)), "Up to the first locomotive or tender"))
      .ToList();
  }

  internal static IReadOnlyList<MenuItem> PickUpItems(Car locomotive)
  {
    return new[]
      {
        MenuItem.Action("Couple back", () => Run(() => Edit(locomotive, orders => orders.Add(OrderStep.CoupleBack()))), "To the cars this train left last"),
        MenuItem.Submenu("Couple to the cars left by", () => LeftByItems(locomotive), "An order in this list that leaves cars")
      }
      .Concat(Enumerable.Range(1, MaxCut)
      .Select(count => MenuItem.Action(count == 1 ? "1 car" : $"{count} cars", () => Run(() => AddPickUp(locomotive, count)), "From the nearest standing cars"))
      .Append(MenuItem.Action("All cars", () => Run(() => AddPickUp(locomotive, Maneuvers.AllCars)), "The whole cut of the nearest standing cars")))
      .ToList();
  }

  private static IReadOnlyList<MenuItem> LeftByItems(Car locomotive)
  {
    var orders = OrderList.Read(locomotive);
    var items = orders.Steps
      .Select((step, index) => (Step: step, Index: index))
      .Where(entry => entry.Step.LeavesCars)
      .Select(entry => MenuItem.Action($"Order {entry.Index + 1}", () => Run(() => AddCoupleTo(locomotive, entry.Index)), Describe(entry.Step)))
      .ToList();
    return items.Count > 0 ? items : new[] { MenuItem.Action("No order here leaves cars yet", null, "Add a Leave cars, Keep or Take order first", disabled: true) };
  }

  internal static void AddCoupleTo(Car locomotive, int index)
  {
    Edit(locomotive, orders =>
    {
      if (index < 0 || index >= orders.Steps.Count || !orders.Steps[index].LeavesCars) return;
      var id = orders.Steps[index].Id;
      if (id == null)
      {
        id = OrderStep.NewId();
        orders.Steps[index] = orders.Steps[index].WithId(id);
      }
      orders.Add(OrderStep.CoupleTo(id));
    });
  }

  internal static List<(int Count, string Label)> TakeOptions()
  {
    var options = new List<(int Count, string Label)> { (Maneuvers.AllCars, "All cars") };
    options.AddRange(Enumerable.Range(1, MaxCut).Select(count => (count, count == 1 ? "1 car" : $"{count} cars")));
    return options;
  }

  internal static void SetTake(Car locomotive, int index, int count)
  {
    Edit(locomotive, orders =>
    {
      if (index >= 0 && index < orders.Steps.Count) orders.Steps[index] = orders.Steps[index].WithTake(count);
    });
  }

  internal static void AddPickUp(Car locomotive, int count)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.PickUp(count)));
  }

  internal static void AddCut(Car locomotive, int count, CutSide side)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.Cut(count, side)));
  }

  internal static void AddWaitLoad(Car locomotive)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.WaitLoad()));
  }

  internal static IReadOnlyList<MenuItem> WaitItems(Car locomotive)
  {
    return new[]
    {
      MenuItem.Action("Until loaded", () => Run(() => AddWaitLoad(locomotive)), "Until every freight car is full"),
      MenuItem.Action("Until unloaded", () => Run(() => AddWaitUnload(locomotive)), "Until every freight car is empty"),
      MenuItem.Action("Until repaired", () => Run(() => Edit(locomotive, orders => orders.Add(OrderStep.WaitRepair()))), "On a repair track, until every car reaches its repair limit"),
      MenuItem.Submenu("Wait time", () => TimeItems(locomotive))
    };
  }

  private static IReadOnlyList<MenuItem> TimeItems(Car locomotive)
  {
    var morning = OrderStep.WaitMorning().WaitHour;
    return new[]
    {
      MenuItem.Action("Until tomorrow", () => Run(() => AddWaitDay(locomotive)), "Until the next day starts at midnight"),
      MenuItem.Action($"Until {Clock(morning)}", () => Run(() => Edit(locomotive, orders => orders.Add(OrderStep.WaitMorning()))), "Until the interchange is first served, where Sleep wakes up"),
      MenuItem.Action("Until a time", () => Run(() => Edit(locomotive, orders => orders.Add(OrderStep.WaitTime(NextHour())))), "Pick the time on the order, to run to a schedule"),
      MenuItem.Submenu("Minutes", () => WaitMinuteOptions.Select(minutes => MenuItem.Action($"{minutes} minutes", () => Run(() => Edit(locomotive, orders => orders.Add(OrderStep.WaitMinutes(minutes)))))).ToList()),
      MenuItem.Submenu("Hours", () => WaitHourOptions.Select(hours => MenuItem.Action(Hours(hours), () => Run(() => AddWaitHours(locomotive, hours)))).ToList())
    };
  }

  private static string Clock(int hour) => new GameDateTime(0, hour).TimeString();

  internal static readonly List<string> HourLabels = Enumerable.Range(0, 24).Select(hour => hour.ToString()).ToList();

  internal static readonly int[] MinuteOptions = Enumerable.Range(0, 12).Select(step => step * 5).ToArray();

  internal static string ClockMinute(int minuteOfDay) => $"{minuteOfDay / 60}:{minuteOfDay % 60:D2}";

  private static int NextHour() => (OrderStep.Minute(TimeWeather.Now) / 60 + 1) % 24 * 60;

  internal static void SetTime(Car locomotive, int index, int minuteOfDay)
  {
    Edit(locomotive, orders =>
    {
      if (index >= 0 && index < orders.Steps.Count && orders.Steps[index].Kind == StepKind.WaitTime) orders.Steps[index] = orders.Steps[index].WithTime(minuteOfDay);
    });
  }

  private static string Hours(int hours) => hours == 1 ? "1 hour" : $"{hours} hours";

  internal static void AddWaitHours(Car locomotive, int hours)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.WaitHours(hours)));
  }

  internal static IReadOnlyList<MenuItem> StationItems(Car locomotive)
  {
    var stops = Passengers.Available();
    if (stops.Count == 0) return new[] { MenuItem.Action("No stations", () => { }, null, true) };
    return stops.Select(stop =>
    {
      var id = stop.identifier;
      return MenuItem.Action(stop.DisplayName, () => Run(() => AddStation(locomotive, id)));
    }).ToList();
  }

  internal static void AddStation(Car locomotive, string stationId)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.ToStation(stationId)));
  }

  internal static IReadOnlyList<MenuItem> RefuelItems(BaseLocomotive locomotive)
  {
    var fuelCar = Fuel.FuelCar(locomotive);
    if (Fuel.Slots(fuelCar).Count == 0) return new[] { MenuItem.Action(fuelCar == null ? "No tender coupled" : "Nothing to refuel", () => { }, null, true) };
    var stops = Fuel.StopsFor(fuelCar);
    if (stops.Count == 0) return new[] { MenuItem.Action("No fuel loaders available", () => { }, null, true) };
    var origin = locomotive.GetCenterPosition(Track.Graph.Shared.Current);
    var entries = new List<(int Group, float Distance, MenuItem Item)>();
    foreach (var pair in Fuel.Pairs(stops))
    {
      var ids = pair.Select(stop => stop.Id).ToList();
      var distance = pair.Min(stop => Away(origin, stop));
      entries.Add((0, distance, MenuItem.Action(Fuel.Join(pair.Select(stop => stop.LoadName), true), () => Run(() => AddRefuel(locomotive, ids)), Where(pair[0].Town, Fuel.Distance(distance)))));
    }
    foreach (var stop in stops)
    {
      var id = stop.Id;
      var distance = Away(origin, stop);
      entries.Add((1, distance, MenuItem.Action(stop.Name, () => Run(() => AddRefuel(locomotive, new[] { id })), Where(stop.Town, Fuel.Distance(distance)))));
    }
    return entries.OrderBy(entry => entry.Group).ThenBy(entry => entry.Distance).Select(entry => entry.Item).ToList();
  }

  private static float Away(Vector3 origin, FuelStop stop) => Vector3.Distance(new Vector3(origin.x, 0f, origin.z), new Vector3(stop.Point.x, 0f, stop.Point.z));

  private static string Where(string town, string text) => string.IsNullOrEmpty(town) ? text : $"{town}, {text}";

  internal static void AddRefuel(Car locomotive, IEnumerable<string> loaderIds)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.Refuel(loaderIds, OrderStep.DefaultBelow)));
  }

  internal static string BelowLabel(int below) => below >= Fuel.Always ? "Always" : $"{below}%";

  internal static void SetBelow(Car locomotive, int index, int below)
  {
    Edit(locomotive, orders =>
    {
      if (index >= 0 && index < orders.Steps.Count) orders.Steps[index] = orders.Steps[index].WithBelow(below);
    });
  }

  internal static void AddWaitUnload(Car locomotive)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.WaitUnload()));
  }

  internal static void AddWaitDay(Car locomotive)
  {
    Edit(locomotive, orders => orders.Add(OrderStep.WaitDay()));
  }

  internal static string TimeLeft(OrderStep step)
  {
    var now = TimeWeather.Now;
    var seconds = (step.UntilMinute ?? step.UntilFrom(now)) * 60.0 - now.TotalSeconds;
    if (seconds <= 0) return "now";
    return $"{GameDateTimeInterval.DeltaStringMinutes(Mathf.CeilToInt((float)(seconds / 60)), GameDateTimeInterval.Style.Short)} left";
  }

  internal static string LoadProgress(Car locomotive, bool unloading)
  {
    var load = Loads.Measure(locomotive);
    if (load.Total == 0) return "no freight cars";
    return unloading ? $"{load.Empty} of {load.Total} empty" : $"{load.Full} of {load.Total} full";
  }

  internal static void SetRunning(Car locomotive, bool running)
  {
    Edit(locomotive, orders =>
    {
      if (running)
      {
        orders.Start();
        return;
      }
      orders.Running = false;
      orders.Status = "Paused";
    });
  }

  internal static void SetRepeat(Car locomotive, bool repeat)
  {
    Edit(locomotive, orders => orders.Repeat = repeat);
  }

  internal static void SetPassSignals(Car locomotive, bool pass)
  {
    Edit(locomotive, orders => orders.PassSignals = pass);
  }

  internal static void Skip(Car locomotive)
  {
    Edit(locomotive, orders =>
    {
      if (orders.Advance()) orders.Finish();
    });
  }

  internal static void Remove(Car locomotive, int index)
  {
    Edit(locomotive, orders => orders.RemoveAt(index));
  }

  internal static void Move(Car locomotive, int index, int offset)
  {
    Edit(locomotive, orders => orders.Move(index, offset));
  }

  internal static void SetSmart(Car locomotive, int index, bool smart)
  {
    Edit(locomotive, orders =>
    {
      if (index >= 0 && index < orders.Steps.Count) orders.Steps[index] = orders.Steps[index].WithSmart(smart);
    });
  }

  internal static void Clear(Car locomotive)
  {
    Edit(locomotive, orders =>
    {
      orders.Clear();
      orders.Running = false;
      orders.Status = null;
    });
  }

  internal static bool HasCopy => _copied.Count > 0;

  internal static string CopyDescription => $"{Orders(_copied.Count)} copied from {_copiedFrom}";

  internal static void Copy(Car locomotive)
  {
    var orders = OrderList.Read(locomotive);
    if (orders.Steps.Count == 0) return;
    _copied = orders.Steps.Select(step => step.Fresh()).ToList();
    _copiedRepeat = orders.Repeat;
    _copiedPassSignals = orders.PassSignals;
    _copiedFrom = locomotive.DisplayName;
    Toast.Present($"Copied {Orders(_copied.Count)} from {_copiedFrom}", ToastPosition.Bottom);
    OrdersWindow.RebuildAll();
  }

  internal static void Paste(Car locomotive)
  {
    if (_copied.Count == 0) return;
    Edit(locomotive, orders =>
    {
      if (orders.Steps.Count == 0)
      {
        orders.Repeat = _copiedRepeat;
        orders.PassSignals = _copiedPassSignals;
      }
      foreach (var step in OrderStep.Renumbered(_copied)) orders.Add(step);
    });
    Toast.Present($"Pasted {Orders(_copied.Count)} into {locomotive.DisplayName}", ToastPosition.Bottom);
  }

  internal static void ResetCopy()
  {
    _copied = new List<OrderStep>();
    _copiedRepeat = false;
    _copiedPassSignals = false;
    _copiedFrom = null;
  }

  private static string Orders(int count) => count == 1 ? "1 order" : $"{count} orders";

  internal static string DefaultJobName(OrderList orders)
  {
    var names = new List<string>();
    foreach (var step in orders.Steps)
    {
      var name = step.Kind switch
      {
        StepKind.Station => Passengers.Name(step.StationId),
        StepKind.Move when step.Waypoint.HasValue && !step.IsCouple => Places.NameFor(step.Waypoint.Value.LocationString),
        _ => null
      };
      if (name != null && name.EndsWith(" waypoint")) name = name.Substring(0, name.Length - " waypoint".Length);
      if (string.IsNullOrEmpty(name) || name == "waypoint" || (names.Count > 0 && names[names.Count - 1] == name)) continue;
      names.Add(name);
    }
    if (names.Count == 0) return "My job";
    var text = string.Join(" - ", names.Take(4));
    if (names.Count > 4) text += " ...";
    return text.Length > SavedJobs.NameLimit ? text.Substring(0, SavedJobs.NameLimit).TrimEnd() : text;
  }

  internal static string JobLine(SavedJob job)
  {
    var details = Orders(job.Steps.Count) + (job.Repeat ? ", repeats" : "");
    return $"{job.Name} <alpha=#99>{details}";
  }

  internal static string JobTooltip(SavedJob job)
  {
    var lines = job.Steps.Take(12).Select((step, index) => $"{index + 1}. {Describe(step)}").ToList();
    if (job.Steps.Count > 12) lines.Add($"and {job.Steps.Count - 12} more");
    if (job.Repeat) lines.Add("Repeats");
    if (job.PassSignals) lines.Add("Smart signals");
    if (job.NamesCars) lines.Add("<alpha=#99>Its couple orders name cars of the game it was saved in; anywhere else they pause.");
    return string.Join("\n", lines);
  }

  internal static void SaveJob(Car locomotive, string name)
  {
    var orders = OrderList.Read(locomotive);
    if (orders.Steps.Count == 0) return;
    name = string.IsNullOrWhiteSpace(name) ? DefaultJobName(orders) : name.Trim();
    if (name.Length > SavedJobs.NameLimit) name = name.Substring(0, SavedJobs.NameLimit).TrimEnd();
    var job = new SavedJob(name, orders.Repeat, orders.PassSignals, orders.Steps.Select(step => step.Fresh()).ToList());
    var replaced = SavedJobs.Save(job);
    Toast.Present(replaced ? $"Updated the saved job \"{name}\" ({Orders(job.Steps.Count)})" : $"Saved \"{name}\" ({Orders(job.Steps.Count)})", ToastPosition.Bottom);
    OrdersWindow.RebuildAll();
  }

  internal static bool LoadJob(Car locomotive, string name, bool replace)
  {
    var job = SavedJobs.Find(name);
    if (job == null) return false;
    if (replace && OrderList.Read(locomotive).Running)
    {
      Toast.Present("Pause the orders first", ToastPosition.Bottom);
      return false;
    }
    Edit(locomotive, orders =>
    {
      if (replace)
      {
        orders.Clear();
        orders.Status = null;
      }
      if (orders.Steps.Count == 0)
      {
        orders.Repeat = job.Repeat;
        orders.PassSignals = job.PassSignals;
      }
      foreach (var step in OrderStep.Renumbered(job.Steps)) orders.Add(step);
    });
    Toast.Present(replace ? $"Loaded \"{job.Name}\" into {locomotive.DisplayName}" : $"Added \"{job.Name}\" to {locomotive.DisplayName}'s orders", ToastPosition.Bottom);
    return true;
  }

  internal static void DeleteJob(string name)
  {
    SavedJobs.Delete(name);
    Toast.Present($"Deleted the saved job \"{name}\"", ToastPosition.Bottom);
    OrdersWindow.RebuildAll();
  }

  internal static void Run(Action action)
  {
    try
    {
      action();
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Train Orders action failed");
      Toast.Present("Train Orders: that didn't work. See the Message Log.", ToastPosition.Bottom);
    }
  }

  private static void Edit(Car locomotive, Action<OrderList> change)
  {
    var orders = OrderList.Read(locomotive);
    change(orders);
    orders.Write(locomotive);
  }

  private static string CarName(string carId)
  {
    return TrainController.Shared != null && TrainController.Shared.TryGetCarForId(carId, out var car) && car != null ? car.DisplayName : carId;
  }
}
