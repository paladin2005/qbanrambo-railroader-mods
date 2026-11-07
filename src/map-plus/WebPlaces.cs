using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Game;
using HarmonyLib;
using Helpers;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using Newtonsoft.Json;
using RollingStock;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebPlaces
{
  private static readonly HashSet<string> Skipped = new();

  internal const string Header = "X-Map-Plus-Place";
  private const string FuelPrefix = "fuel:";
  private const int PerformanceDays = 7;
  private const float NearbyFuel = 40f;

  internal static byte[] Detail(string id)
  {
    if (!WebMapData.InGame) return Json(new { ok = false, reason = "Load a game to see the map" });
    if (string.IsNullOrEmpty(id)) return Json(new { ok = false, reason = "Nothing selected" });
    try
    {
      return id.StartsWith(FuelPrefix, StringComparison.Ordinal) ? FuelDetail(id.Substring(FuelPrefix.Length)) : IndustryDetail(id);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Warning($"The web map couldn't describe {id}: {exception.Message}");
      return Json(new { ok = false, reason = "Couldn't read this place" });
    }
  }

  private static byte[] IndustryDetail(string id)
  {
    var ops = OpsController.Shared;
    var industry = ops.IndustryForId(id);
    if (industry == null) return Json(new { ok = false, reason = "That place is gone" });
    var engines = PlaceData.Engines();
    var town = Places.Towns().FirstOrDefault(entry => entry.Places.Any(place => place.Industry == industry));
    var places = town.Places?.Where(place => place.Industry == industry).ToList() ?? new List<Place>();
    var counts = PlaceData.CountCars();
    var empty = new CarBucket();
    CarBucket For(Place place) => counts.TryGetValue(place.SpanKey, out var bucket) ? bucket : empty;
    var present = new List<Car>();
    var buffer = new List<Car>();
    var tracks = new List<object>();
    foreach (var place in places)
    {
      var here = new HashSet<Car>();
      foreach (var span in place.Spans)
      {
        buffer.Clear();
        TrainController.Shared.GetCarsOnSpan(span, buffer);
        here.UnionWith(buffer.Where(car => car != null));
      }
      foreach (var car in here)
      {
        if (!present.Contains(car)) present.Add(car);
      }
      tracks.Add(new { n = place.Name, s = For(place).Spotted.Count, o = For(place).Coming.Count, now = here.Count });
    }
    var coming = places.SelectMany(place => For(place).Coming).Distinct().Select(car => CarRow(car, TrainOrTown(car))).ToList();
    var sections = new List<object>();
    foreach (var (component, context) in industry.EnumerateComponentContexts(0f))
    {
      if (component == null || !component.IsProgressionEnabled) continue;
      var fields = component.PanelFields(context).Select(field => new[] { field.Label, field.Text }).ToList();
      if (fields.Count > 0) sections.Add(new { n = component.ShortName(industry), f = fields });
    }
    var (makes, takes) = PlaceData.Loads(industry);
    object interchangeInfo = null;
    if (industry.Components.Any(component => component is Interchange || component is InterchangedIndustryLoader))
    {
      var now = TimeWeather.Now.TotalSeconds;
      var last = industry.Storage?.InterchangeLastServiced;
      var extra = industry.Storage?.InterchangeExtraScheduled;
      interchangeInfo = new
      {
        last = last.HasValue ? Math.Round(Math.Max(0, now - last.Value.TotalSeconds) / 3600.0, 1) : (double?)null,
        extra = extra.HasValue ? Math.Round((extra.Value.TotalSeconds - now) / 3600.0, 1) : (double?)null
      };
    }
    var performance = (industry.PerformanceHistory ?? new Dictionary<int, float>())
      .OrderByDescending(entry => entry.Key)
      .Take(PerformanceDays)
      .Select(entry => new object[] { entry.Key, entry.Value })
      .ToList();
    return Json(new
    {
      ok = true,
      kind = "industry",
      i = industry.identifier,
      n = industry.DisplayName,
      k = PlaceData.Category(industry, engines),
      t = town.Area?.name ?? industry.Area?.name,
      ct = industry.usesContract ? industry.Contract?.Tier : null,
      nct = industry.usesContract ? industry.NextContract?.Tier : null,
      w = industry.Storage?.Warnings?.Values.ToList() ?? new List<string>(),
      makes = makes.OrderBy(name => name).ToList(),
      takes = takes.OrderBy(name => name).ToList(),
      ic = interchangeInfo,
      px = Passengers(industry.PassengerStop, true),
      sec = sections,
      tracks,
      present = present.Select(car => CarRow(car, StateAt(car, industry))).ToList(),
      coming,
      perf = performance
    });
  }

  private static byte[] FuelDetail(string id)
  {
    var stops = PlaceData.FuelStops();
    var point = stops.FirstOrDefault(candidate => candidate.Id == id);
    if (point == null) return Json(new { ok = false, reason = "That fuel stop is gone" });
    var stock = PlaceData.Stock(point, out var empty);
    var source = string.IsNullOrEmpty(point.Source) ? null : OpsController.Shared?.IndustryForId(point.Source);
    var supplies = source?.Storage == null ? null : source.Storage.Loads()
      .Where(load => load != null)
      .Select(load => new[] { string.IsNullOrWhiteSpace(load.description) ? load.id : load.description, load.QuantityString(source.Storage.QuantityInStorage(load)) })
      .ToList();
    var nearby = stops
      .Where(other => other != point && other.Loader != null && Vector3.Distance(other.Point, point.Point) < NearbyFuel)
      .Select(other => new { n = other.Name, k = other.Kind, s = PlaceData.Stock(other, out var otherEmpty), e = otherEmpty })
      .ToList();
    return Json(new { ok = true, kind = "fuel", i = point.Id, n = point.Name, k = point.Kind, t = point.Town, s = stock, e = empty, src = source?.DisplayName, supplies, nearby });
  }

  private static object Passengers(PassengerStop stop, bool full)
  {
    if (stop == null || !stop.IsEnabledReachable) return null;
    var waiting = stop.Waiting
      .Where(entry => entry.Value.Total > 0)
      .OrderByDescending(entry => entry.Value.Total)
      .Select(entry => new object[] { PassengerStop.NameForIdentifier(entry.Key), entry.Value.Total })
      .ToList();
    var last = stop.LastServed;
    double? hours = last.HasValue ? Math.Round(Math.Max(0, TimeWeather.Now.TotalSeconds - last.Value.TotalSeconds) / 3600.0, 1) : null;
    var total = stop.Waiting.Values.Sum(info => info.Total);
    if (!full) return new { total, d = waiting, h = hours, flag = stop.flagStop };
    return new { total, d = waiting, h = hours, flag = stop.flagStop, pop = stop.basePopulation + stop.AdditionalPopulation, code = stop.timetableCode };
  }

  private static object CarRow(Car car, string state)
  {
    var freight = car.CarType.IsFreight;
    return new
    {
      i = car.id,
      n = car.DisplayName,
      model = car.DefinitionInfo?.Metadata?.Name,
      load = CarInfo.Loads(car) ?? (freight ? "Empty" : null),
      s = state,
      color = freight ? "#" + ColorUtility.ToHtmlStringRGB(CarInfo.MapColor(car)) : null
    };
  }

  private static string TrainOrTown(Car car)
  {
    var engine = Consist.Of(car).OfType<BaseLocomotive>().FirstOrDefault();
    if (engine != null) return $"In {engine.DisplayName}'s train";
    var town = PlaceData.WhereIs(car);
    return town == null ? null : town == "off the map" ? "Off the map" : $"At {town}";
  }

  private static string StateAt(Car car, Industry industry)
  {
    var ops = OpsController.Shared;
    if (ops == null || !ops.TryGetDestinationInfo(car, out var name, out var arrived, out _, out var position)) return car.CarType.IsFreight ? "No waybill" : null;
    var identifier = position.Identifier ?? "";
    if (identifier == industry.identifier || identifier.StartsWith(industry.identifier + ".", StringComparison.Ordinal)) return arrived ? "Spotted here" : "Waybilled here, not spotted yet";
    var destination = CarInfo.Destination(car);
    return destination != null ? $"Ready, {destination.Describe()}" : $"Ready, to {name}";
  }

  internal static (string Name, string Kind, string Town)? FuelInfo(string id)
  {
    var point = PlaceData.FuelStop(id);
    return point == null ? null : (point.Name, point.Kind, point.Town);
  }

  internal static byte[] Build()
  {
    if (!WebMapData.InGame) return Json(new { ok = false, reason = "Load a game to see the map" });
    var stops = PlaceData.FuelStops();
    var counts = PlaceData.CountCars();
    var engines = PlaceData.Engines();
    var industries = new List<object>();
    foreach (var town in Places.Towns())
    {
      foreach (var group in town.Places.Where(place => place.Industry != null).GroupBy(place => place.Industry))
      {
        try
        {
          industries.Add(Describe(group.Key, town.Area, group.ToList(), counts, engines));
        }
        catch (Exception exception)
        {
          if (Skipped.Add(group.Key.identifier ?? "")) MapPlusMod.Log?.Warning($"The web map skipped {group.Key.DisplayName}: {exception.Message}");
        }
      }
    }
    var fuel = stops
      .Where(point => point.Loader != null)
      .Select(point =>
      {
        var stock = PlaceData.Stock(point, out var empty);
        return new { i = point.Id, n = point.Name, k = point.Kind, t = point.Town, x = R(point.Point.x), z = R(point.Point.z), s = stock, e = empty };
      })
      .ToList();
    return Json(new { ok = true, sv = WebMapData.BuiltVersion, industries, fuel });
  }

  private static object Describe(Industry industry, Area area, List<Place> places, Dictionary<string, CarBucket> counts, HashSet<string> engines)
  {
    var center = Vector3.zero;
    foreach (var place in places) center += place.Position;
    center /= places.Count;
    var empty = new CarBucket();
    CarBucket For(Place place) => counts.TryGetValue(place.SpanKey, out var bucket) ? bucket : empty;
    var tracks = places.Select(place => new { n = place.Name, s = For(place).Spotted.Count, o = For(place).Coming.Count }).ToList();
    var spotted = places.SelectMany(place => For(place).Spotted.Select(car => new object[] { car.id, place.Name })).ToList();
    var coming = places.SelectMany(place => For(place).Coming.Select(car => new object[] { car.id, PlaceData.WhereIs(car) })).ToList();
    var segments = new HashSet<int>();
    var index = WebMapData.SegmentIndex;
    foreach (var place in places)
    {
      foreach (var span in place.Spans)
      {
        foreach (var segment in span.GetSegments())
        {
          if (segment != null && index.TryGetValue(segment, out var value)) segments.Add(value);
        }
      }
    }
    var sections = new List<object>();
    foreach (var (component, context) in industry.EnumerateComponentContexts(0f))
    {
      if (component == null || !component.IsProgressionEnabled) continue;
      var fields = component.PanelFields(context).Select(field => new[] { field.Label, field.Text }).ToList();
      if (fields.Count > 0) sections.Add(new { n = component.ShortName(industry), f = fields });
    }
    var warnings = industry.Storage?.Warnings?.Values.ToList() ?? new List<string>();
    int? tier = industry.usesContract ? industry.Contract?.Tier : null;
    var passengers = Passengers(industry.PassengerStop, false);
    return new
    {
      i = industry.identifier,
      n = industry.DisplayName,
      t = area.name,
      k = PlaceData.Category(industry, engines),
      x = R(center.x),
      z = R(center.z),
      tr = tracks,
      sec = sections,
      w = warnings,
      ct = tier,
      cs = spotted,
      co = coming,
      sg = segments.OrderBy(value => value).ToList(),
      px = passengers
    };
  }

  private static float R(float value) => (float)Math.Round(value, 1);

  private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
}
