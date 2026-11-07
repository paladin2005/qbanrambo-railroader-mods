using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Model;
using Track;
using UI;
using UI.Common;
using UnityEngine;

namespace QbanRambo.Goto;

internal static class RefuelMenu
{
  private const float PairDistance = 100f;
  private const float MetersPerMile = 1609.344f;
  private const float FeetPerMeter = 3.28084f;
  private const float FeetBelowMiles = 0.1f;

  internal static MenuItem Item(BaseLocomotive locomotive)
  {
    var fuelCar = Fuel.FuelCar(locomotive);
    var slots = Fuel.Slots(fuelCar);
    if (slots.Count == 0) return MenuItem.Submenu("Refuel", Array.Empty<MenuItem>(), fuelCar == null ? "No tender coupled" : "Nothing to refuel", disabled: true);
    var levels = string.Join(", ", slots.Select(entry => $"{Fuel.LoadName(entry.Load)} {Fuel.Level(entry.Slot, entry.Load) * 100f:0}%"));
    return MenuItem.Submenu("Refuel", () => GotoMenu.Safe(() => Items(locomotive, fuelCar)), levels);
  }

  private static IReadOnlyList<MenuItem> Items(BaseLocomotive locomotive, Car fuelCar)
  {
    var loads = Fuel.Slots(fuelCar).Select(entry => entry.Load.id).ToList();
    var stops = Fuel.Stops().Where(stop => loads.Contains(stop.Load.id)).ToList();
    if (stops.Count == 0) return new[] { MenuItem.Action("No fuel loaders available", null, disabled: true) };
    var origin = locomotive.GetCenterPosition(Graph.Shared.Current);
    var entries = new List<(int Group, float Distance, MenuItem Item)>();
    foreach (var pair in Pairs(stops)) entries.Add((0, pair.Min(stop => Away(origin, stop)), PairItem(locomotive, fuelCar, pair, origin)));
    foreach (var stop in stops) entries.Add((1, Away(origin, stop), StopItem(locomotive, fuelCar, stop, origin)));
    return entries.OrderBy(entry => entry.Group).ThenBy(entry => entry.Distance).Select(entry => entry.Item).ToList();
  }

  private static List<List<FuelStop>> Pairs(List<FuelStop> stops)
  {
    var pairs = new List<List<FuelStop>>();
    var groups = stops.GroupBy(stop => stop.Load.id).OrderBy(group => group.First().LoadName).ToList();
    if (groups.Count < 2) return pairs;
    foreach (var first in groups[0])
    {
      var pair = new List<FuelStop> { first };
      foreach (var group in groups.Skip(1))
      {
        var nearest = group.OrderBy(stop => Apart(first, stop)).First();
        if (Apart(first, nearest) <= PairDistance) pair.Add(nearest);
      }
      if (pair.Count > 1) pairs.Add(pair);
    }
    return pairs;
  }

  private static MenuItem PairItem(BaseLocomotive locomotive, Car fuelCar, List<FuelStop> pair, Vector3 origin)
  {
    var town = pair.Select(stop => stop.Town).FirstOrDefault(name => !string.IsNullOrEmpty(name));
    var full = pair.All(stop => IsFull(fuelCar, stop));
    var usable = pair.Any(stop => !IsFull(fuelCar, stop) && Fuel.InStock(stop));
    var subtitle = Where(town, full ? "full" : usable ? Distance(pair.Min(stop => Away(origin, stop))) : "out of stock");
    return MenuItem.Action(Join(pair.Select(stop => stop.LoadName).ToList()), () => Send(locomotive, pair), subtitle, disabled: !usable);
  }

  private static MenuItem StopItem(BaseLocomotive locomotive, Car fuelCar, FuelStop stop, Vector3 origin)
  {
    var full = IsFull(fuelCar, stop);
    var stock = Fuel.InStock(stop);
    var subtitle = Where(stop.Town, full ? "full" : stock ? Distance(Away(origin, stop)) : "out of stock");
    return MenuItem.Action(stop.Name, () => Send(locomotive, new List<FuelStop> { stop }), subtitle, disabled: full || !stock);
  }

  private static void Send(BaseLocomotive locomotive, List<FuelStop> stops)
  {
    try
    {
      RefuelWatch.Begin(locomotive, stops);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Error(exception, "Refuel failed");
      Toast.Present("Refuel failed. See the Message Log.", ToastPosition.Bottom);
    }
  }

  private static bool IsFull(Car fuelCar, FuelStop stop) => Fuel.Slot(fuelCar, stop.Load) is not { } slot || Fuel.IsFull(slot, stop.Load);

  private static float Apart(FuelStop first, FuelStop second) => Vector3.Distance(first.Point.ZeroY(), second.Point.ZeroY());

  private static float Away(Vector3 origin, FuelStop stop) => Vector3.Distance(origin.ZeroY(), stop.Point.ZeroY());

  internal static string Distance(float meters)
  {
    var miles = meters / MetersPerMile;
    return miles < FeetBelowMiles ? $"{meters * FeetPerMeter:0} ft" : $"{miles:0.0} mi";
  }

  private static string Where(string town, string text) => string.IsNullOrEmpty(town) ? text : $"{town}, {text}";

  private static string Join(List<string> names)
  {
    var words = names.Select((name, index) => index == 0 ? name : name.ToLowerInvariant()).ToList();
    return words.Count == 1 ? words[0] : $"{string.Join(", ", words.Take(words.Count - 1))} and {words[words.Count - 1]}";
  }
}
