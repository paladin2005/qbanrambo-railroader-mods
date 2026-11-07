using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Model;
using Model.AI;
using Model.Ops;
using Newtonsoft.Json;
using Track;
using Track.Signals;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebMapData
{
  private const float MetersPerSecondToMph = 2.23694f;
  private const float SignalListSeconds = 5f;
  private const float FuseeReach = 8f;
  private const string NoGame = "Load a game to see the map";

  private static readonly List<(CTCSignal Signal, Vector3 Position, Vector2 Direction)> Signals = new();
  private static readonly List<TrackNode> Switches = new();
  private static int _version;
  private static int _trackVersion = -1;
  private static byte[] _track;
  private static float _signalsAt = float.MinValue;

  internal static Dictionary<TrackSegment, int> SegmentIndex { get; private set; } = new();
  internal static int BuiltVersion => _trackVersion;

  internal static bool InGame => TrainController.Shared != null && Graph.Shared != null && OpsController.Shared != null;

  internal static void Tick()
  {
    if (!InGame)
    {
      _track = null;
      _trackVersion = -1;
      Signals.Clear();
      Switches.Clear();
      SegmentIndex = new Dictionary<TrackSegment, int>();
      return;
    }
    _version = TrackVersion();
  }

  internal static byte[] Track()
  {
    if (!InGame) return Json(new { ok = false, reason = NoGame });
    if (_track != null && _trackVersion == _version) return _track;
    var graph = Graph.Shared;
    var segments = new List<object>();
    var indices = new Dictionary<TrackSegment, int>();
    float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
    foreach (var segment in graph.Segments)
    {
      if (segment?.a == null || segment.b == null) continue;
      var a = segment.a.Position;
      var b = segment.b.Position;
      TrackSegment.ComputeBezierHandles(a, segment.a.Rotation, b, segment.b.Rotation, out var p1, out var p2);
      var built = TrackInfo.IsBuilt(segment);
      var state = built ? 0 : segment.Available ? 2 : 1;
      var kind = TrackPainter.IsIndustry(segment) ? 2 : TrackPainter.IsMainline(segment) ? 1 : 0;
      var flags = ((segment.flags & TrackSegment.Flags.Bridge) != 0 ? 1 : 0) | ((segment.flags & TrackSegment.Flags.Tunnel) != 0 ? 2 : 0) | ((segment.flags & TrackSegment.Flags.Yard) != 0 ? 4 : 0) | ((segment.flags & TrackSegment.Flags.BridgeSupportsSteel) != 0 ? 8 : 0);
      indices[segment] = segments.Count;
      segments.Add(new object[] { R(a.x), R(a.z), R(p1.x), R(p1.z), R(p2.x), R(p2.z), R(b.x), R(b.z), state, kind, built ? TrackInfo.ClassOf(segment) : 0, flags });
      if (!built) continue;
      minX = Mathf.Min(minX, Mathf.Min(a.x, b.x));
      minZ = Mathf.Min(minZ, Mathf.Min(a.z, b.z));
      maxX = Mathf.Max(maxX, Mathf.Max(a.x, b.x));
      maxZ = Mathf.Max(maxZ, Mathf.Max(a.z, b.z));
    }
    SegmentIndex = indices;
    Switches.Clear();
    var switches = new List<object>();
    foreach (var node in graph.Nodes)
    {
      if (node == null || !graph.IsSwitch(node) || !graph.DecodeSwitchAt(node, out _, out var normal, out var reverse)) continue;
      if (normal == null || reverse == null || !indices.TryGetValue(normal, out var normalIndex) || !indices.TryGetValue(reverse, out var reverseIndex)) continue;
      Switches.Add(node);
      switches.Add(new object[] { R(node.Position.x), R(node.Position.z), normalIndex, (int)normal.EndForNode(node), reverseIndex, (int)reverse.EndForNode(node) });
    }
    var towns = Places.Towns().Select(town => new
    {
      n = town.Area.name,
      x = R(town.Area.Position.x),
      z = R(town.Area.Position.z),
      r = R(town.Area.radius),
      p = town.Places.Select(place => new { n = place.Name, x = R(place.Position.x), z = R(place.Position.z) }).ToList()
    }).ToList();
    var bounds = minX <= maxX ? new[] { R(minX), R(minZ), R(maxX), R(maxZ) } : null;
    var rivers = WebRivers.Paths();
    _track = Json(new { ok = true, version = _version, bounds, segments, switches, towns, rivers });
    _trackVersion = _version;
    return _track;
  }

  internal static byte[] Fusee(string point, string action, string key)
  {
    if (!InGame) return Json(new { ok = false, text = NoGame });
    try
    {
      if (!Fusees.CanEdit) return Json(new { ok = false, text = "Your role can't place or put out fusees" });
      if (action == "remove" && !string.IsNullOrEmpty(key))
      {
        Fusees.Remove(key);
        return Json(new { ok = true, text = "Fusee put out" });
      }
      var parts = (point ?? "").Split(',');
      if (parts.Length != 2 || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) return Json(new { ok = false, text = "No track there" });
      var terrain = Map.Runtime.MapManager.Instance;
      var game = new Vector3(x, 0f, z);
      if (terrain != null && terrain.TryFindTerrainPointForXZ(game, out var ground)) game = ground;
      if (!Graph.Shared.TryGetLocationFromGamePoint(game, FuseeReach, out var location)) return Json(new { ok = false, text = "No track there" });
      if (action == "remove")
      {
        if (!Fusees.TryNear(location, out var near)) return Json(new { ok = false, text = "No fusee there" });
        Fusees.Remove(near);
        return Json(new { ok = true, text = "Fusee put out" });
      }
      Fusees.Place(location);
      return Json(new { ok = true, text = "Fusee placed" });
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "The web map couldn't place or put out a fusee");
      return Json(new { ok = false, text = "That didn't work. See the Message Log." });
    }
  }

  internal static byte[] Select(string id)
  {
    if (!InGame) return Json(new { ok = false, reason = NoGame });
    var controller = TrainController.Shared;
    if (string.IsNullOrEmpty(id) || !controller.TryGetCarForId(id, out var car) || car == null) return Json(new { ok = false, reason = "No such train" });
    var target = car is BaseLocomotive ? car : Consist.Of(car).OfType<BaseLocomotive>().FirstOrDefault() ?? car;
    if (controller.SelectedCar != target) controller.SelectedCar = target;
    return Json(new { ok = true });
  }

  internal static byte[] State()
  {
    if (!InGame) return Json(new { ok = false, reason = NoGame });
    var controller = TrainController.Shared;
    var groups = new Dictionary<Car, string>();
    var order = new Dictionary<Car, int>();
    var locomotives = controller.Cars.OfType<BaseLocomotive>().Where(locomotive => locomotive != null).ToList();
    foreach (var locomotive in locomotives)
    {
      if (groups.ContainsKey(locomotive)) continue;
      var index = 0;
      foreach (var car in Consist.Of(locomotive))
      {
        if (groups.ContainsKey(car)) continue;
        groups[car] = locomotive.id;
        order[car] = index++;
      }
    }
    var cars = new List<object>();
    foreach (var car in controller.Cars)
    {
      try
      {
        if (car == null || car.IsInBardo) continue;
        var body = car.LastBodyPosition;
        if (body == null || body.Length < 2) continue;
        var front = body[0];
        var rear = body[1];
        var kind = car is BaseLocomotive ? 1 : car.CarType.IsTender ? 2 : 0;
        string alert = null;
        var level = 0;
        if (car is BaseLocomotive locomotive)
        {
          var alerts = TrainAlerts.For(locomotive);
          if (alerts.Count > 0)
          {
            level = alerts[0].Level;
            alert = Hex(TrainAlerts.ColorFor(level));
          }
        }
        else if (CarInfo.Alert(car, groups.ContainsKey(car)) is { } color)
        {
          alert = Hex(color);
        }
        groups.TryGetValue(car, out var group);
        var position = order.TryGetValue(car, out var value) ? value : 0;
        cars.Add(new object[] { car.id, R(front.x), R(front.z), R(rear.x), R(rear.z), R(car.carLength), car.Ident.ReportingMark, car.Ident.RoadNumber, kind, kind == 0 ? Hex(CarInfo.MapColor(car)) : null, alert, level, group, position });
      }
      catch (Exception)
      {
      }
    }
    var locos = locomotives.Select(locomotive => new
    {
      i = locomotive.id,
      n = locomotive.DisplayName,
      s = Mathf.RoundToInt(Mathf.Abs(locomotive.velocity) * MetersPerSecondToMph),
      m = ModeOf(locomotive)
    }).OrderBy(locomotive => locomotive.n).ToList();
    RefreshSignals();
    var signals = Signals
      .Where(entry => entry.Signal != null)
      .Select(entry => new object[] { entry.Signal.id, R(entry.Position.x), R(entry.Position.z), R(entry.Direction.x, 3), R(entry.Direction.y, 3), (int)entry.Signal.CurrentAspect, entry.Signal.IsIntermediate ? 1 : 0 })
      .ToList();
    var thrown = new StringBuilder(Switches.Count);
    foreach (var node in Switches) thrown.Append(node != null && node.isThrown ? '1' : '0');
    var chosen = controller.SelectedCar;
    string sel = null;
    if (chosen is BaseLocomotive chosenLocomotive) sel = chosenLocomotive.id;
    else if (chosen != null && groups.TryGetValue(chosen, out var chosenGroup)) sel = chosenGroup;
    var fl = Fusees.All().Select(fusee => new object[] { R(fusee.Position.x), R(fusee.Position.z), fusee.Key }).ToList();
    return Json(new { ok = true, version = _version, cars, locos, signals, sw = thrown.ToString(), sv = _trackVersion, sel, fl });
  }

  internal static byte[] Routes()
  {
    if (!InGame) return Json(new { ok = false, reason = NoGame });
    var routes = new List<object>();
    foreach (var locomotive in TrainController.Shared.Cars.OfType<BaseLocomotive>())
    {
      try
      {
        var points = locomotive != null ? RouteOverlay.GamePoints(locomotive) : null;
        if (points == null || points.Count < 2) continue;
        routes.Add(new { i = locomotive.id, p = points.SelectMany(point => new[] { R(point.x), R(point.z) }).ToList() });
      }
      catch (Exception)
      {
      }
    }
    return Json(new { ok = true, routes });
  }

  internal static byte[] Details()
  {
    if (!InGame) return Json(new { ok = false, reason = NoGame });
    var cars = new Dictionary<string, object>();
    foreach (var car in TrainController.Shared.Cars)
    {
      try
      {
        if (car == null || car.IsInBardo) continue;
        var tooltip = CarInfo.Tooltip(car);
        cars[car.id] = new { t = tooltip.Title, b = tooltip.Text };
      }
      catch (Exception)
      {
      }
    }
    var signals = new Dictionary<string, string>();
    foreach (var entry in Signals)
    {
      if (entry.Signal == null || string.IsNullOrEmpty(entry.Signal.id)) continue;
      signals[entry.Signal.id] = entry.Signal.IsIntermediate ? "Automatic signal" : $"{entry.Signal.DisplayName} signal";
    }
    return Json(new { ok = true, cars, signals });
  }

  private static int ModeOf(BaseLocomotive locomotive)
  {
    try
    {
      return (int)new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Mode;
    }
    catch (Exception)
    {
      return 0;
    }
  }

  private static void RefreshSignals()
  {
    if (Time.unscaledTime - _signalsAt < SignalListSeconds && Signals.All(entry => entry.Signal != null)) return;
    _signalsAt = Time.unscaledTime;
    Signals.Clear();
    var graph = Graph.Shared;
    foreach (var signal in UnityEngine.Object.FindObjectsByType<CTCSignal>(FindObjectsSortMode.None))
    {
      if (signal == null || !signal.isActiveAndEnabled) continue;
      if (SignalIcons.TryPose(graph, signal, out var position, out var direction)) Signals.Add((signal, position, direction));
    }
  }

  private static int TrackVersion()
  {
    unchecked
    {
      var hash = 17;
      foreach (var segment in Graph.Shared.Segments)
      {
        if (segment == null) continue;
        hash = hash * 31 + (int)segment.ConstructionState;
        hash = hash * 31 + (segment.Available ? 1 : 0);
        hash = hash * 31 + segment.PostedTrackClass;
        hash = hash * 31 + (TrackPainter.IsIndustry(segment) ? 2 : TrackPainter.IsMainline(segment) ? 1 : 0);
      }
      return hash;
    }
  }

  private static float R(float value, int digits = 1) => (float)Math.Round(value, digits);

  private static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

  private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
}
