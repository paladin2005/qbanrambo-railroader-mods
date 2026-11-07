using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class CarInfo
{
  private const float MetersToFeet = 3.28084f;
  private const float MetersPerSecondToMph = 2.23694f;
  private const float CouplerGap = 1f;
  private const float LowOil = 0.5f;
  private const float SpottedValue = 0.6f;
  private const float CriticalOil = 0.25f;

  private static readonly Color HotboxAlert = new(0.95f, 0.15f, 0.1f);
  private static readonly Color OilAlert = new(1f, 0.55f, 0.05f);
  private static readonly Color HandbrakeAlert = new(0.75f, 0.35f, 0.95f);

  private static readonly MethodInfo FuelCarMethod = typeof(SteamLocomotive).GetMethod("FuelCar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

  internal static Color MapColor(Car car)
  {
    if (!car.CarType.IsFreight) return Color.white;
    var destination = Destination(car);
    if (destination == null) return Color.white;
    var color = destination.Area != null ? destination.Area.tagColor : Color.white;
    if (!destination.Arrived) return Vivid(color);
    if (color.maxColorComponent < 0.99f) return new Color(color.r, color.g, color.b, 1f);
    Color.RGBToHSV(color, out var hue, out var saturation, out var value);
    return Color.HSVToRGB(hue, saturation, value * SpottedValue);
  }

  internal static Color? Alert(Car car, bool inTrain)
  {
    if (car.HasHotbox) return HotboxAlert;
    if (car.EnableOiling && car.Oiled < CriticalOil) return OilAlert;
    if (inTrain && car.air != null && car.air.handbrakeApplied) return HandbrakeAlert;
    return null;
  }

  internal static TooltipInfo Tooltip(Car car)
  {
    return car is BaseLocomotive locomotive ? LocomotiveTooltip(locomotive) : CarTooltip(car);
  }

  internal static TooltipInfo PlaceTooltip(Place place) => new(place.Name, string.Join("\n", PlaceLines(place)));

  internal static List<string> PlaceLines(Place place)
  {
    var lines = new List<string> { place.Area.name };
    var spotted = 0;
    var onTheWay = 0;
    var ops = OpsController.Shared;
    var cars = TrainController.Shared?.Cars;
    if (ops != null && cars != null)
    {
      foreach (var car in cars)
      {
        if (car == null || !ops.TryGetDestinationInfo(car, out _, out var arrived, out _, out var position)) continue;
        if (Places.SpanKey(position.Spans) != place.SpanKey) continue;
        if (arrived) spotted++;
        else onTheWay++;
      }
    }
    if (spotted > 0) lines.Add($"{Count(spotted, "car", "cars")} spotted");
    if (onTheWay > 0) lines.Add($"{Count(onTheWay, "car", "cars")} on the way");
    return lines;
  }

  private static TooltipInfo CarTooltip(Car car)
  {
    var lines = new List<string>();
    var model = car.DefinitionInfo?.Metadata?.Name;
    if (!string.IsNullOrEmpty(model)) lines.Add(model);
    lines.Add(Loads(car) ?? "Empty");
    lines.Add(Destination(car)?.Describe() ?? "No waybill");
    lines.AddRange(Warnings(car));
    return new TooltipInfo(car.DisplayName, string.Join("\n", lines));
  }

  private static TooltipInfo LocomotiveTooltip(BaseLocomotive locomotive)
  {
    var consist = Consist.Of(locomotive).ToList();
    var cars = consist.Where(car => car is not BaseLocomotive && !car.CarType.IsTender).ToList();
    var feet = Mathf.RoundToInt((consist.Sum(car => car.carLength) + CouplerGap * (consist.Count - 1)) * MetersToFeet);
    var mph = Mathf.RoundToInt(Mathf.Abs(locomotive.velocity * MetersPerSecondToMph));
    var lines = new List<string>();
    if (MapPlusMod.TrainAlerts.IsOn)
    {
      foreach (var alert in TrainAlerts.For(locomotive)) lines.Add($"<color=#{ColorUtility.ToHtmlStringRGB(TrainAlerts.ColorFor(alert.Level))}>{alert.Text}</color>");
    }
    lines.Add($"{mph} mph, {Count(cars.Count, "car", "cars")}, {feet} ft");
    var fuel = Fuel(locomotive);
    if (fuel != null) lines.Add(fuel);
    var destinations = cars
      .Select(Destination)
      .Where(destination => destination != null && !destination.Arrived)
      .GroupBy(destination => destination.Name)
      .OrderByDescending(group => group.Count())
      .Select(group => $"{group.Key} ({group.Count()})")
      .ToList();
    if (destinations.Count > 0) lines.Add("To " + string.Join(", ", destinations));
    var handbrakes = consist.Count(car => car.air != null && car.air.handbrakeApplied);
    if (handbrakes > 0) lines.Add(Count(handbrakes, "handbrake", "handbrakes") + " set");
    var hotboxes = consist.Count(car => car.HasHotbox);
    if (hotboxes > 0) lines.Add(Count(hotboxes, "hotbox", "hotboxes") + "!");
    return new TooltipInfo(locomotive.DisplayName, string.Join("\n", lines));
  }

  internal static Destination Destination(Car car)
  {
    var ops = OpsController.Shared;
    if (ops == null || !ops.TryGetDestinationInfo(car, out var name, out var arrived, out _, out var position)) return null;
    Area area = null;
    try
    {
      area = ops.AreaForCarPosition(position);
    }
    catch (Exception)
    {
    }
    return new Destination(name, area, arrived);
  }

  internal static string Loads(Car car)
  {
    var slots = car.Definition?.LoadSlots;
    if (slots == null) return null;
    var parts = new List<string>();
    for (var index = 0; index < slots.Count; index++)
    {
      var info = car.GetLoadInfo(index);
      if (!info.HasValue || string.IsNullOrEmpty(info.Value.LoadId) || info.Value.Quantity <= 0.001f) continue;
      var loadId = info.Value.LoadId;
      var name = CarLoadLibrary.TryGetLoad(loadId, out var load) ? load.description : loadId;
      var capacity = slots[index].CalculateMaximumCapacity(loadId, car);
      parts.Add(capacity > 0f ? $"{name} {Percent(info.Value.Quantity / capacity)}" : name);
    }
    return parts.Count == 0 ? null : string.Join(", ", parts);
  }

  internal static List<(string Name, int Percent)> FuelLevels(BaseLocomotive locomotive)
  {
    var levels = new List<(string Name, int Percent)>();
    var fuelCar = locomotive is SteamLocomotive ? FuelCarMethod?.Invoke(locomotive, null) as Car : locomotive;
    var slots = fuelCar?.Definition?.LoadSlots;
    if (slots == null) return levels;
    for (var index = 0; index < slots.Count; index++)
    {
      var loadId = slots[index].RequiredLoadIdentifier;
      if (string.IsNullOrEmpty(loadId)) continue;
      var capacity = slots[index].CalculateMaximumCapacity(loadId, fuelCar);
      if (capacity <= 0f) continue;
      var quantity = fuelCar.GetLoadInfo(index)?.Quantity ?? 0f;
      var name = CarLoadLibrary.TryGetLoad(loadId, out var load) ? load.description : loadId;
      levels.Add((name, Mathf.RoundToInt(Mathf.Clamp01(quantity / capacity) * 100f)));
    }
    return levels;
  }

  private static string Fuel(BaseLocomotive locomotive)
  {
    var fuelCar = locomotive is SteamLocomotive ? FuelCarMethod?.Invoke(locomotive, null) as Car : locomotive;
    var slots = fuelCar?.Definition?.LoadSlots;
    if (slots == null) return null;
    var parts = new List<string>();
    for (var index = 0; index < slots.Count; index++)
    {
      var loadId = slots[index].RequiredLoadIdentifier;
      if (string.IsNullOrEmpty(loadId)) continue;
      var capacity = slots[index].CalculateMaximumCapacity(loadId, fuelCar);
      if (capacity <= 0f) continue;
      var quantity = fuelCar.GetLoadInfo(index)?.Quantity ?? 0f;
      var name = CarLoadLibrary.TryGetLoad(loadId, out var load) ? load.description : loadId;
      parts.Add($"{name} {Percent(quantity / capacity)}");
    }
    return parts.Count == 0 ? null : string.Join(", ", parts);
  }

  private static IEnumerable<string> Warnings(Car car)
  {
    if (car.HasHotbox) yield return "Hotbox!";
    else if (car.EnableOiling && car.Oiled < LowOil) yield return $"Oil {Percent(car.Oiled)}";
    if (car.air != null && car.air.handbrakeApplied) yield return "Handbrake set";
  }

  private static Color Vivid(Color color)
  {
    var max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
    return max < 0.01f ? Color.white : new Color(color.r / max, color.g / max, color.b / max, 1f);
  }

  private static string Percent(float value) => $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";

  private static string Count(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";
}

internal sealed class Destination
{
  public Destination(string name, Area area, bool arrived)
  {
    Name = name;
    Area = area;
    Arrived = arrived;
  }

  public string Name { get; }
  public Area Area { get; }
  public bool Arrived { get; }

  public string Describe()
  {
    var town = Area != null && !string.IsNullOrEmpty(Area.name) && !Name.Contains(Area.name) ? $", {Area.name}" : "";
    return $"To {Name}{town}{(Arrived ? " (spotted)" : "")}";
  }
}
