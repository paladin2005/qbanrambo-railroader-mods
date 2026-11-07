using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Messages;
using Model;
using Model.AI;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal readonly struct TrainAlert
{
  public TrainAlert(int level, string text)
  {
    Level = level;
    Text = text;
  }

  public int Level { get; }
  public string Text { get; }
}

internal static class TrainAlerts
{
  private const int Warning = 1;
  private const int Problem = 2;
  private const int Danger = 3;
  private const float LowFuel = 0.2f;
  private const float NoFuel = 0.05f;
  private const float SignalWaitSeconds = 60f;
  private const int NamesShown = 2;

  internal static readonly Color DangerColor = new(0.95f, 0.15f, 0.1f);
  private static readonly Color ProblemColor = new(1f, 0.55f, 0.05f);
  private static readonly Color WarningColor = new(1f, 0.85f, 0.2f);

  private static readonly MethodInfo FuelCarMethod = typeof(SteamLocomotive).GetMethod("FuelCar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
  private static readonly Dictionary<string, float> WaitingSince = new();

  internal static void Reset() => WaitingSince.Clear();

  internal static Color ColorFor(int level) => level >= Danger ? DangerColor : level == Problem ? ProblemColor : WarningColor;

  internal static Color? BadgeColor(BaseLocomotive locomotive)
  {
    var alerts = For(locomotive);
    return alerts.Count == 0 ? null : ColorFor(alerts[0].Level);
  }

  internal static List<TrainAlert> For(BaseLocomotive locomotive)
  {
    var alerts = new List<TrainAlert>();
    var consist = Consist.Of(locomotive).ToList();
    var derailed = consist.Where(car => car.IsDerailed).ToList();
    if (derailed.Count > 0) alerts.Add(new TrainAlert(Danger, $"Derailed: {Names(derailed)}"));
    var hotboxes = consist.Where(car => car.HasHotbox).ToList();
    if (hotboxes.Count > 0) alerts.Add(new TrainAlert(Danger, $"Hotbox: {Names(hotboxes)}"));
    AddFuel(locomotive, alerts);
    AddEngineer(locomotive, alerts);
    return alerts.OrderByDescending(alert => alert.Level).ToList();
  }

  private static void AddFuel(BaseLocomotive locomotive, List<TrainAlert> alerts)
  {
    var fuelCar = locomotive is SteamLocomotive ? FuelCarMethod?.Invoke(locomotive, null) as Car : locomotive;
    var slots = fuelCar?.Definition?.LoadSlots;
    if (slots == null) return;
    for (var index = 0; index < slots.Count; index++)
    {
      var loadId = slots[index].RequiredLoadIdentifier;
      if (string.IsNullOrEmpty(loadId)) continue;
      var capacity = slots[index].CalculateMaximumCapacity(loadId, fuelCar);
      if (capacity <= 0f) continue;
      var level = Mathf.Clamp01((fuelCar.GetLoadInfo(index)?.Quantity ?? 0f) / capacity);
      if (level >= LowFuel) continue;
      var name = (CarLoadLibrary.TryGetLoad(loadId, out var load) ? load.description : loadId).ToLowerInvariant();
      var percent = Mathf.RoundToInt(level * 100f);
      alerts.Add(level <= NoFuel ? new TrainAlert(Danger, $"Out of {name} ({percent}%)") : new TrainAlert(Problem, $"Low on {name} ({percent}%)"));
    }
  }

  private static void AddEngineer(BaseLocomotive locomotive, List<TrainAlert> alerts)
  {
    var persistence = new AutoEngineerPersistence(locomotive.KeyValueObject);
    var orders = persistence.Orders;
    if (orders.Mode == AutoEngineerMode.Off)
    {
      WaitingSince.Remove(locomotive.id);
      return;
    }
    var status = persistence.PlannerStatus;
    if (!string.IsNullOrEmpty(status) && (status.EndsWith(" applied") || status == "Check the brake line" || status == "Brake applied beyond brake line"))
    {
      alerts.Add(new TrainAlert(Problem, $"Engineer stopped: {status}"));
    }
    var signal = persistence.ContextualOrders.FirstOrDefault(order => order.Order == ContextualOrder.OrderValue.PassSignal);
    if (signal.Order != ContextualOrder.OrderValue.PassSignal || !locomotive.IsStopped(1f))
    {
      WaitingSince.Remove(locomotive.id);
      return;
    }
    if (!WaitingSince.TryGetValue(locomotive.id, out var since))
    {
      WaitingSince[locomotive.id] = Time.time;
      return;
    }
    var waited = Time.time - since;
    if (waited < SignalWaitSeconds) return;
    var place = SignalIcons.NameFor(signal.Context);
    var minutes = Mathf.FloorToInt(waited / 60f);
    alerts.Add(new TrainAlert(Warning, $"Waiting at a red signal{(place != null ? $" at {place}" : "")}, {minutes} min"));
  }

  private static string Names(List<Car> cars)
  {
    var names = string.Join(", ", cars.Take(NamesShown).Select(car => car.DisplayName));
    return cars.Count > NamesShown ? $"{names} and {cars.Count - NamesShown} more" : names;
  }
}
