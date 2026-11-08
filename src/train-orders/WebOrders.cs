using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UI;
using UI.EngineControls;

namespace QbanRambo.TrainOrders;

internal static class WebOrders
{
  internal const string HookKey = "qbanrambo.train-orders.web";
  private const int ApiVersion = 1;
  private const string GotoMenuKey = "qbanrambo.goto.menu";

  internal static readonly Func<string, string, string, string> Hook = Handle;

  private static readonly (string Id, string Title, string Tip)[] Menus =
  {
    ("goto", "Go to", "Add a trip to a town location. Passenger stations become station orders, like the Stations button."),
    ("pickup", "Pick up cars", "Add an order to pick up cars: the train couples to the nearest standing cars and takes that many, leaving the rest."),
    ("cut", "Leave cars", "Add an order to leave cars behind: the front or rear ones, a few or all of them."),
    ("wait", "Wait", "Add a wait: until the cars are loaded or unloaded, until tomorrow, or for some hours."),
    ("goback", "Go back", "Add a loop: go back to an earlier order until a time or a number of times, then go on."),
    ("speed", "Set speed", "Add a speed: the trips after it run at that speed until another Set speed order."),
    ("stations", "Stations", "Add a stop at a passenger station."),
    ("refuel", "Refuel", "Add a refuel at a water, coal or diesel loader."),
    ("turntable", "Turntable", "Add a turntable move: turn the engine around, park it on a stall or leave on another track. Engines only, no cars.")
  };

  private static bool _warned;

  internal static Car Capturing { get; private set; }

  private static string Handle(string locomotiveId, string action, string args)
  {
    try
    {
      var controller = TrainController.Shared;
      if (controller == null || string.IsNullOrEmpty(locomotiveId) || !controller.TryGetCarForId(locomotiveId, out var car) || car is not BaseLocomotive locomotive)
      {
        return Json(new { ok = false, text = "That train is no longer on the map" });
      }
      var input = string.IsNullOrEmpty(args) ? new JObject() : JObject.Parse(args);
      string text = null;
      switch (action)
      {
        case null:
        case "view":
          break;
        case "menu":
          return Json(new { ok = true, items = MenuView(locomotive, input) });
        case "ghost":
          return Json(new { ok = true, api = ApiVersion, cars = Ghost(locomotive, Int(input, "index")) });
        case "capture":
          Capturing = Bool(input, "on") ? locomotive : null;
          return Json(new { ok = true });
        case "pick":
          text = Pick(locomotive, input);
          break;
        case "move":
          OrderActions.Move(locomotive, Int(input, "index"), Int(input, "offset"));
          break;
        case "remove":
          OrderActions.Remove(locomotive, Int(input, "index"));
          break;
        case "run":
          OrderActions.SetRunning(locomotive, Bool(input, "on"));
          break;
        case "skip":
          OrderActions.Skip(locomotive);
          break;
        case "clear":
          OrderActions.Clear(locomotive);
          break;
        case "repeat":
          OrderActions.SetRepeat(locomotive, Bool(input, "on"));
          break;
        case "pass":
          OrderActions.SetPassSignals(locomotive, Bool(input, "on"));
          break;
        case "copy":
          OrderActions.Copy(locomotive);
          break;
        case "paste":
          OrderActions.Paste(locomotive);
          break;
        case "below":
          OrderActions.SetBelow(locomotive, Int(input, "index"), Int(input, "value"));
          break;
        case "exit":
          OrderActions.SetExit(locomotive, Int(input, "index"), NullableInt(input, "value"));
          break;
        case "track":
          OrderActions.SetTrack(locomotive, Int(input, "index"), Str(input, "value"));
          break;
        case "take":
          OrderActions.SetTake(locomotive, Int(input, "index"), Int(input, "value"));
          break;
        case "time":
          OrderActions.SetTime(locomotive, Int(input, "index"), Int(input, "value"));
          break;
        case "smart":
          OrderActions.SetSmart(locomotive, Int(input, "index"), Bool(input, "on"));
          break;
        case "save":
          OrderActions.SaveJob(locomotive, Str(input, "name"));
          break;
        case "load":
          if (!OrderActions.LoadJob(locomotive, Str(input, "name"), true)) text = "Pause the orders first";
          break;
        case "add":
          OrderActions.LoadJob(locomotive, Str(input, "name"), false);
          break;
        case "delete":
          OrderActions.DeleteJob(Str(input, "name"));
          break;
        case "waypoint":
          text = AddWaypoint(locomotive, Str(input, "location"));
          break;
        default:
          return Json(new { ok = false, text = "Unknown action" });
      }
      return Json(View(locomotive, text));
    }
    catch (Exception exception)
    {
      if (!_warned)
      {
        _warned = true;
        TrainOrdersMod.Log?.Error(exception, $"The web map's Train Orders panel failed on {action}");
      }
      return Json(new { ok = false, text = "That didn't work. See the Message Log." });
    }
  }

  private static object View(BaseLocomotive locomotive, string text)
  {
    var orders = OrderList.Read(locomotive);
    ForecastRun forecast;
    try
    {
      forecast = Forecast.Run(locomotive, orders);
    }
    catch (Exception)
    {
      forecast = new ForecastRun();
    }
    var started = orders.Running || orders.Status != null;
    var (front, rear) = Maneuvers.CarsAround(locomotive);
    var hasGoto = AppDomain.CurrentDomain.GetData(GotoMenuKey) is Func<object, IReadOnlyList<MenuItem>>;
    var rows = orders.Steps.Select((step, index) => new
    {
      t = $"{index + 1}. {OrderActions.RowText(step, orders)}",
      s = index < orders.Current ? "done" : index == orders.Current && started ? "current" : "plain",
      d = index == orders.Current ? Progress(locomotive, step) : null,
      c = Controls(step),
      f = forecast.Results.TryGetValue(index, out var result) ? ConsistStrip.Web(result) : null
    }).ToList();
    return new
    {
      ok = true,
      api = ApiVersion,
      text,
      name = locomotive.DisplayName,
      status = OrderActions.Status(orders),
      engineer = OrdersWindow.EngineerStatus(locomotive),
      train = front > 0 && rear > 0 ? $"{Cars(front)} in front, {Cars(rear)} behind" : null,
      repeat = orders.Repeat,
      pass = orders.PassSignals,
      running = orders.Running,
      finished = orders.Finished,
      count = orders.Steps.Count,
      paste = OrderActions.HasCopy ? OrderActions.CopyDescription : null,
      menus = Menus.Where(menu => menu.Id != "goto" || hasGoto).Select(menu => new { id = menu.Id, title = menu.Title, tip = menu.Tip }).ToList(),
      rows,
      jobs = SavedJobs.All().Select(job => new { n = job.Name, l = OrderActions.JobLine(job), tip = OrderActions.JobTooltip(job) }).ToList(),
      jobName = OrderActions.DefaultJobName(orders)
    };
  }

  private static List<object> Ghost(BaseLocomotive locomotive, int index)
  {
    var cars = new List<object>();
    var graph = Track.Graph.Shared;
    if (graph == null) return cars;
    var orders = OrderList.Read(locomotive);
    var track = index >= 0 && index < orders.Steps.Count ? Ghosts.TurntableTrack(orders.Steps[index]) : null;
    if (track != null)
    {
      foreach (var segment in track)
      {
        if (segment == null) continue;
        var length = segment.GetLength();
        var steps = Math.Max(1, (int)Math.Ceiling(length / 5f));
        var points = new List<double>();
        for (var i = 0; i <= steps; i++)
        {
          segment.GetPositionRotationAtDistance(length * i / steps, Track.TrackSegment.End.A, Track.PositionAccuracy.Standard, out var position, out _);
          points.Add(Math.Round(position.x, 1));
          points.Add(Math.Round(position.z, 1));
        }
        cars.Add(new { p = points, e = false, l = false });
      }
      return cars;
    }
    var run = Forecast.Run(locomotive, orders);
    if (!run.States.TryGetValue(index, out var state) || state == null) return cars;
    void Add(ForecastCar car, List<Track.Location> path, bool left)
    {
      var points = new List<double>();
      foreach (var location in path)
      {
        var position = graph.GetPosition(location);
        points.Add(Math.Round(position.x, 1));
        points.Add(Math.Round(position.z, 1));
      }
      if (points.Count >= 4) cars.Add(new { p = points, e = car.Car != null && (car.Car.CarType.IsLocomotive || car.Car.CarType.IsTender), l = left });
    }
    foreach (var (car, spot) in state.Left) Add(car, spot.Path, true);
    foreach (var (car, spot) in state.Train) Add(car, spot.Reversed().Path, false);
    return cars;
  }

  private static string Progress(BaseLocomotive locomotive, OrderStep step)
  {
    try
    {
      return step.Kind switch
      {
        StepKind.WaitLoad => OrderActions.LoadProgress(locomotive, false),
        StepKind.WaitUnload => OrderActions.LoadProgress(locomotive, true),
        StepKind.WaitDay or StepKind.WaitHours or StepKind.WaitMinutes or StepKind.WaitTime => OrderActions.TimeLeft(step),
        StepKind.WaitRepair => Repairs.Progress(locomotive),
        StepKind.Move or StepKind.Station => Dispatcher.IsBoarding(locomotive) ? "boarding" : null,
        StepKind.Refuel => Dispatcher.RefuelDetail(locomotive),
        StepKind.Turntable => Dispatcher.TurntableDetail(locomotive),
        _ => null
      };
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static List<object> Controls(OrderStep step)
  {
    var controls = new List<object>();
    if (step.Kind == StepKind.Refuel)
    {
      controls.Add(new { k = "below", l = "Only below", o = OrderActions.BelowOptions.Select(value => new { v = value.ToString(), l = OrderActions.BelowLabel(value) }).ToList(), v = (OrderActions.BelowOptions.Contains(step.Count) ? step.Count : OrderStep.DefaultBelow).ToString() });
    }
    if (step.IsCouple)
    {
      controls.Add(new { k = "take", l = "Then", o = OrderActions.TakeOptions().Select(option => new { v = option.Count.ToString(), l = option.Label }).ToList(), v = OrderActions.TakeValue(step).ToString() });
    }
    if (OrderActions.HasTime(step))
    {
      controls.Add(new { k = "time", l = "Time", hours = OrderActions.HourLabels, minutes = OrderActions.MinuteOptions, v = step.Count });
    }
    if (step.Kind == StepKind.Station)
    {
      var tracks = OrderActions.TrackOptions(step.StationId);
      if (tracks != null && tracks.Count > 2) controls.Add(new { k = "track", l = "Track", o = tracks.Select(option => new { v = option.Track ?? "", l = option.Label }).ToList(), v = step.Track ?? "" });
    }
    if (step.Kind == StepKind.Turntable)
    {
      var exits = OrderActions.ExitOptions(Turntables.Find(step.TurntableId));
      controls.Add(new { k = "exit", l = "", o = exits.Select(option => new { v = option.Exit?.ToString() ?? "", l = option.Label }).ToList(), v = step.Exit?.ToString() ?? "" });
    }
    if (step.Kind == StepKind.Move && step.Smart.HasValue)
    {
      controls.Add(new { k = "smart", l = "Smart entrance", v = step.Smart == true });
    }
    return controls;
  }

  private static IReadOnlyList<MenuItem> Root(BaseLocomotive locomotive, string menu)
  {
    return menu switch
    {
      "goto" => AppDomain.CurrentDomain.GetData(GotoMenuKey) is Func<object, IReadOnlyList<MenuItem>> gotoMenu ? gotoMenu(locomotive) : null,
      "pickup" => OrderActions.PickUpItems(locomotive),
      "cut" => OrderActions.CutItems(locomotive),
      "wait" => OrderActions.WaitItems(locomotive),
      "speed" => OrderActions.SpeedItems(locomotive),
      "goback" => OrderActions.GoBackItems(locomotive),
      "stations" => OrderActions.StationItems(locomotive),
      "refuel" => OrderActions.RefuelItems(locomotive),
      "turntable" => OrderActions.TurntableItems(locomotive),
      _ => null
    };
  }

  private static IReadOnlyList<MenuItem> Walk(BaseLocomotive locomotive, JObject input, out MenuItem? chosen)
  {
    chosen = null;
    var items = Root(locomotive, Str(input, "menu"));
    var path = input["path"] as JArray ?? new JArray();
    foreach (var token in path)
    {
      if (items == null) return null;
      var index = token.Value<int>();
      if (index < 0 || index >= items.Count) return null;
      var item = items[index];
      chosen = item;
      items = item.HasChildren ? item.Children() : null;
    }
    return items;
  }

  private static List<object> MenuView(BaseLocomotive locomotive, JObject input)
  {
    Capturing = locomotive;
    try
    {
      var items = Walk(locomotive, input, out _) ?? Array.Empty<MenuItem>();
      return items.Select(item => (object)new { l = item.Label, s = item.Subtitle, d = item.Disabled, c = item.Check == DropdownMenu.CheckState.Checked, sub = item.HasChildren }).ToList();
    }
    finally
    {
      Capturing = null;
    }
  }

  private static string Pick(BaseLocomotive locomotive, JObject input)
  {
    Capturing = locomotive;
    try
    {
      Walk(locomotive, input, out var chosen);
      if (chosen is not { } item) return "That's no longer in the menu";
      if (item.Disabled) return item.Subtitle ?? "Not available";
      if (item.HasChildren) return null;
      item.OnChosen?.Invoke();
      return null;
    }
    finally
    {
      Capturing = null;
    }
  }

  private static string AddWaypoint(BaseLocomotive locomotive, string location)
  {
    if (string.IsNullOrEmpty(location)) return "No track there";
    Capturing = locomotive;
    try
    {
      var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
      helper.SetWaypointOrder(new OrderWaypoint(location, null));
      return null;
    }
    finally
    {
      Capturing = null;
    }
  }

  private static string Cars(int count) => count == 1 ? "1 car" : $"{count} cars";

  private static int Int(JObject input, string key) => input[key]?.Type is JTokenType.Integer or JTokenType.Float ? input[key].Value<int>() : int.TryParse(input[key]?.ToString(), out var value) ? value : 0;

  private static int? NullableInt(JObject input, string key) => int.TryParse(input[key]?.ToString(), out var value) ? value : null;

  private static bool Bool(JObject input, string key) => input[key]?.Type == JTokenType.Boolean ? input[key].Value<bool>() : input[key]?.ToString() == "true";

  private static string Str(JObject input, string key)
  {
    var value = input[key]?.ToString();
    return string.IsNullOrEmpty(value) ? null : value;
  }

  private static string Json(object value) => JsonConvert.SerializeObject(value);
}
