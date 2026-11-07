using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using RollingStock;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal sealed class FuelStop
{
  public CarLoadTargetLoader Loader;
  public Load Load;
  public string Id;
  public string Name;
  public string Kind;
  public string Town;
  public string Source;
  public Vector3 Point;
}

internal sealed class CarBucket
{
  public readonly List<Car> Spotted = new();
  public readonly List<Car> Coming = new();
}

internal static class PlaceData
{
  private const float FuelListSeconds = 15f;

  private static readonly FieldInfo SourceIndustryField = AccessTools.Field(typeof(CarLoadTargetLoader), "sourceIndustryId");
  private static readonly FieldInfo TooltipTitleField = AccessTools.Field(typeof(IndustryContentHoverable), "tooltipTitle");
  private static readonly HashSet<string> Timber = new() { "rr/logs", "rr/pulpwood" };
  private static readonly HashSet<string> TrackMaterials = new() { "rr/ballast", "rr/ties", "rr/rails", "rr/bridge-parts-wood", "rr/bridge-parts-steel", "rr/concrete" };
  private static readonly HashSet<string> Fuels = new() { "rr/oil", "rr/diesel-fuel" };
  private static readonly Dictionary<string, string> FuelKinds = new() { { "rr/water", "water" }, { "rr/coal", "coal" }, { "rr/diesel-fuel", "diesel" } };
  private static readonly List<FuelStop> Stops = new();
  private static float _fuelAt = float.MinValue;

  internal static void Reset()
  {
    Stops.Clear();
    _fuelAt = float.MinValue;
  }

  internal static List<FuelStop> FuelStops()
  {
    Refresh();
    return Stops;
  }

  internal static FuelStop FuelStop(string id)
  {
    if (Stops.Count == 0) Refresh();
    return Stops.FirstOrDefault(stop => stop.Id == id);
  }

  internal static HashSet<string> Engines() => new(FuelStops().Select(stop => stop.Source).Where(id => !string.IsNullOrEmpty(id)));

  internal static string Category(Industry industry, HashSet<string> engines)
  {
    var components = industry.Components.Where(component => component != null && component.IsProgressionEnabled).ToList();
    if (components.Any(component => component is Interchange || component is InterchangedIndustryLoader)) return "interchange";
    if (engines.Contains(industry.identifier)) return "engine";
    var produced = new HashSet<string>();
    var consumed = new HashSet<string>();
    foreach (var component in components)
    {
      switch (component)
      {
        case IndustryLoaderBase loader:
          Add(produced, loader.loadId);
          break;
        case IndustrySeller seller:
          Add(produced, seller.loadId);
          break;
        case IndustryUnloader unloader:
          Add(consumed, unloader.loadId);
          break;
        case FormulaicIndustryComponent formula:
          foreach (var term in formula.inputTerms) Add(consumed, term.loadId);
          foreach (var term in formula.outputTerms) Add(produced, term.loadId);
          break;
      }
    }
    if (industry.PassengerStop != null && produced.Count == 0 && consumed.Count == 0) return "station";
    if (produced.Contains("rr/coal")) return "mine";
    if (produced.Overlaps(Timber)) return "logging";
    if (produced.Overlaps(TrackMaterials) || consumed.Overlaps(TrackMaterials)) return "mow";
    if (produced.Overlaps(Fuels) || consumed.Overlaps(Fuels)) return "oil";
    if (produced.Count > 0 && consumed.Count > 0) return "mill";
    if (industry.PassengerStop != null) return "station";
    return "freight";
  }

  internal static (HashSet<string> Makes, HashSet<string> Takes) Loads(Industry industry)
  {
    var makes = new HashSet<string>();
    var takes = new HashSet<string>();
    foreach (var component in industry.Components.Where(component => component != null && component.IsProgressionEnabled))
    {
      switch (component)
      {
        case IndustryLoaderBase loader:
          AddName(makes, loader.loadId);
          break;
        case IndustrySeller seller:
          AddName(makes, seller.loadId);
          break;
        case IndustryUnloader unloader:
          AddName(takes, unloader.loadId);
          break;
        case FormulaicIndustryComponent formula:
          foreach (var term in formula.inputTerms) AddName(takes, term.loadId);
          foreach (var term in formula.outputTerms) AddName(makes, term.loadId);
          break;
        case InterchangedIndustryLoader interchange:
          AddName(makes, interchange.loadId);
          break;
      }
    }
    return (makes, takes);
  }

  internal static Dictionary<string, CarBucket> CountCars()
  {
    var counts = new Dictionary<string, CarBucket>();
    var ops = OpsController.Shared;
    var cars = TrainController.Shared?.Cars;
    if (ops == null || cars == null) return counts;
    foreach (var car in cars)
    {
      try
      {
        if (car == null || !ops.TryGetDestinationInfo(car, out _, out var arrived, out _, out var position)) continue;
        var key = Places.SpanKey(position.Spans);
        if (key == null) continue;
        if (!counts.TryGetValue(key, out var bucket))
        {
          bucket = new CarBucket();
          counts[key] = bucket;
        }
        (arrived ? bucket.Spotted : bucket.Coming).Add(car);
      }
      catch (Exception)
      {
      }
    }
    return counts;
  }

  internal static string Stock(FuelStop stop, out bool empty)
  {
    empty = false;
    if (string.IsNullOrEmpty(stop.Source)) return null;
    var industry = OpsController.Shared?.IndustryForId(stop.Source);
    if (industry?.Storage == null) return null;
    var quantity = industry.Storage.QuantityInStorage(stop.Load);
    empty = quantity <= 0f;
    return stop.Load.QuantityString(quantity);
  }

  internal static string WhereIs(Car car)
  {
    try
    {
      if (car.IsInBardo) return "off the map";
      var body = car.LastBodyPosition;
      if (body == null || body.Length == 0) return null;
      return OpsController.Shared?.ClosestAreaForGamePosition(body[0])?.name;
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static void Refresh()
  {
    if (Time.unscaledTime - _fuelAt < FuelListSeconds && Stops.All(stop => stop.Loader != null)) return;
    _fuelAt = Time.unscaledTime;
    Stops.Clear();
    var ops = OpsController.Shared;
    foreach (var loader in UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(FindObjectsSortMode.None))
    {
      try
      {
        if (loader == null || !loader.isActiveAndEnabled) continue;
        var load = loader.load;
        var id = loader.keyValueObject != null ? loader.keyValueObject.RegisteredId : null;
        if (load == null || string.IsNullOrEmpty(id) || !FuelKinds.TryGetValue(load.id, out var kind)) continue;
        var root = loader.GetComponentInParent<Game.Builtins.CarLoaderBuiltin>();
        var owner = root != null ? root.gameObject : loader.gameObject;
        var point = WorldTransformer.WorldToGame(loader.transform.position);
        Stops.Add(new FuelStop
        {
          Loader = loader,
          Load = load,
          Id = id,
          Name = NameFor(owner, load),
          Kind = kind,
          Town = ops?.ClosestAreaForGamePosition(point)?.name,
          Source = SourceIndustryField?.GetValue(loader) as string,
          Point = point
        });
      }
      catch (Exception)
      {
      }
    }
  }

  private static string NameFor(GameObject owner, Load load)
  {
    var hoverable = owner.GetComponentInChildren<IndustryContentHoverable>(true);
    var title = hoverable != null ? TooltipTitleField?.GetValue(hoverable) as string : null;
    return string.IsNullOrWhiteSpace(title) ? (string.IsNullOrWhiteSpace(load.description) ? load.id : load.description) : title;
  }

  private static void Add(HashSet<string> set, string loadId)
  {
    if (!string.IsNullOrEmpty(loadId)) set.Add(loadId);
  }

  private static void AddName(HashSet<string> names, string loadId)
  {
    if (string.IsNullOrEmpty(loadId)) return;
    names.Add(CarLoadLibrary.TryGetLoad(loadId, out var load) && !string.IsNullOrWhiteSpace(load.description) ? load.description : loadId);
  }
}
