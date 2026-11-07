using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Helpers;
using Model;
using Model.AI;
using Track;
using Track.Signals;
using UI.Common;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class Signals
{
  private const float StuckSeconds = 10f;
  private const float RetrySeconds = 15f;
  private const float CheckSeconds = 2f;
  private const float MovingReach = 1000f;
  private const float StandingReach = 400f;
  private const float HoldReach = 45f;
  private const float HeldSeconds = 2f;
  private const float MovingSpeed = 0.5f;
  private const int MaxSegments = 800;

  private static readonly Dictionary<string, float> LastPass = new();
  private static readonly Dictionary<string, float> NextCheck = new();
  private static readonly Dictionary<string, string> Waiting = new();
  private static Dictionary<string, CTCSignal> _byId;

  internal static void Reset()
  {
    LastPass.Clear();
    NextCheck.Clear();
    Waiting.Clear();
    _byId = null;
  }

  internal static void TryPass(BaseLocomotive locomotive)
  {
    var panel = CTCPanelController.Shared;
    if (panel == null || panel.SystemMode != SystemMode.ABS || !locomotive.IsStopped(StuckSeconds)) return;
    var persistence = new AutoEngineerPersistence(locomotive.KeyValueObject);
    if (!persistence.Orders.Waypoint.HasValue) return;
    var signal = WaitingSignal(persistence);
    if (signal == null) return;
    var now = Time.unscaledTime;
    if (LastPass.TryGetValue(locomotive.id, out var passedAt) && now - passedAt < RetrySeconds) return;
    if (NextCheck.TryGetValue(locomotive.id, out var next) && now < next) return;
    NextCheck[locomotive.id] = now + CheckSeconds;
    var key = $"{locomotive.id}|{signal.id}";
    var blocker = Blocker(locomotive, signal, out var other);
    if (blocker != null)
    {
      if (Waiting.TryGetValue(key, out var last) && last == blocker) return;
      Waiting[key] = blocker;
      TrainOrdersMod.Log?.Info($"{locomotive.DisplayName} waits at the red signal {signal.id}: {blocker}");
      if (other) Toast.Present($"{locomotive.DisplayName} waits at the red signal at {signal.DisplayName}: {blocker}", ToastPosition.Bottom);
      return;
    }
    Waiting.Remove(key);
    LastPass[locomotive.id] = now;
    StateManager.ApplyLocal(new AutoEngineerContextualOrder(locomotive.id, ContextualOrder.OrderValue.PassSignal, signal.id));
    TrainOrdersMod.Log?.Info($"{locomotive.DisplayName} passes the red signal {signal.id}");
    Toast.Present($"{locomotive.DisplayName} passing the red signal at {signal.DisplayName}, no other train ahead", ToastPosition.Bottom);
  }

  private static CTCSignal WaitingSignal(AutoEngineerPersistence persistence)
  {
    var order = persistence.ContextualOrders.FirstOrDefault(candidate => candidate.Order == ContextualOrder.OrderValue.PassSignal);
    if (order.Order != ContextualOrder.OrderValue.PassSignal || string.IsNullOrEmpty(order.Context)) return null;
    return Find(order.Context);
  }

  private static CTCSignal Find(string id)
  {
    if (_byId != null && _byId.TryGetValue(id, out var signal) && signal != null) return signal;
    _byId = UnityEngine.Object.FindObjectsByType<CTCSignal>(FindObjectsSortMode.None)
      .Where(candidate => !string.IsNullOrEmpty(candidate.id))
      .GroupBy(candidate => candidate.id)
      .ToDictionary(group => group.Key, group => group.First());
    return _byId.TryGetValue(id, out signal) ? signal : null;
  }

  private static string Blocker(BaseLocomotive locomotive, CTCSignal signal, out bool other)
  {
    other = false;
    var interlocking = signal.Interlocking;
    if (interlocking == null) return "not an interlocking signal";
    try
    {
      var train = new HashSet<Car>(locomotive.EnumerateCoupled());
      if (!BlocksClear(signal, train)) return "another train is in the blocks ahead";
      var graph = Graph.Shared;
      var controller = TrainController.Shared;
      if (graph == null || controller == null) return "the track isn't loaded";
      var area = Area(graph, interlocking, train);
      var farSide = new HashSet<Car>(interlocking.outlets
        .Where(outlet => outlet.direction == signal.direction)
        .SelectMany(outlet => outlet.Blocks)
        .Where(block => block != null)
        .SelectMany(block => block.CarsInBlock()));
      var seen = new HashSet<Car>(train);
      foreach (var engine in controller.Cars.OfType<BaseLocomotive>().ToList())
      {
        if (engine == null || engine.set == null || seen.Contains(engine)) continue;
        var cars = engine.EnumerateCoupled().ToList();
        seen.UnionWith(cars);
        var engines = cars.OfType<BaseLocomotive>().ToList();
        var moving = cars.Any(car => Mathf.Abs(car.velocity) > MovingSpeed);
        if (!moving && !engines.Any(IsRunning)) continue;
        var reach = moving ? MovingReach : StandingReach;
        if (!cars.Any(car => farSide.Contains(car) || Near(area, car, reach))) continue;
        if (!moving && HeldAt(engines, cars, interlocking, out var held, out var heldSignal))
        {
          if (!GoesFirst(held, heldSignal, cars, locomotive)) continue;
          other = true;
          return $"{held.DisplayName} goes first";
        }
        other = true;
        return $"{engine.DisplayName} is {(moving ? "moving" : "working")} near the interlocking";
      }
      return null;
    }
    catch (Exception exception)
    {
      return $"the track around it couldn't be checked ({exception.Message})";
    }
  }

  private static bool BlocksClear(CTCSignal signal, HashSet<Car> train)
  {
    var interlocking = signal.Interlocking;
    if (interlocking == null) return false;
    var blocks = new HashSet<CTCBlock>(interlocking.Blocks);
    var lined = false;
    for (var index = 0; index < interlocking.routes.Count; index++)
    {
      var (outlet, _, isLined) = interlocking.BlockAndNexSignal(index, signal.direction);
      if (!isLined) continue;
      lined = true;
      blocks.UnionWith(outlet);
    }
    return lined && blocks.Where(block => block != null).All(block => block.CarsInBlock().All(train.Contains));
  }

  private static Dictionary<TrackSegment, float> Area(Graph graph, CTCInterlocking interlocking, HashSet<Car> train)
  {
    var own = new HashSet<TrackSegment>();
    foreach (var car in train)
    {
      if (car.LocationA.segment != null) own.Add(car.LocationA.segment);
      if (car.LocationB.segment != null) own.Add(car.LocationB.segment);
    }
    var area = new Dictionary<TrackSegment, float>();
    var queue = new Queue<(TrackSegment Segment, TrackNode From, float Distance)>();
    foreach (var node in interlocking.SwitchNodes.Where(node => node != null))
    {
      foreach (var segment in graph.SegmentsConnectedTo(node)) queue.Enqueue((segment, node, 0f));
    }
    while (queue.Count > 0 && area.Count < MaxSegments)
    {
      var (segment, from, distance) = queue.Dequeue();
      if (segment == null || own.Contains(segment)) continue;
      if (area.TryGetValue(segment, out var known) && known <= distance) continue;
      area[segment] = distance;
      var reach = distance + segment.GetLength();
      if (reach >= MovingReach) continue;
      var node = segment.GetOtherNode(from);
      if (node == null) continue;
      foreach (var next in graph.SegmentsConnectedTo(node))
      {
        if (next != segment) queue.Enqueue((next, node, reach));
      }
    }
    return area;
  }

  private static bool Near(Dictionary<TrackSegment, float> area, Car car, float reach)
  {
    if (car.IsInBardo) return false;
    return Within(area, car.LocationA.segment, reach) || Within(area, car.LocationB.segment, reach);
  }

  private static bool Within(Dictionary<TrackSegment, float> area, TrackSegment segment, float reach)
  {
    return segment != null && area.TryGetValue(segment, out var distance) && distance < reach;
  }

  private static bool IsRunning(BaseLocomotive engine)
  {
    var orders = new AutoEngineerPersistence(engine.KeyValueObject).Orders;
    if (orders.Mode == AutoEngineerMode.Road && orders.MaxSpeedMph > 0) return true;
    if (orders.Mode == AutoEngineerMode.Waypoint && orders.Waypoint.HasValue) return true;
    var list = OrderList.Read(engine);
    if (!list.Running || list.Finished) return false;
    return list.Steps[list.Current].Kind is not (StepKind.WaitDay or StepKind.WaitHours or StepKind.WaitTime);
  }

  private static bool HeldAt(List<BaseLocomotive> engines, List<Car> cars, CTCInterlocking interlocking, out BaseLocomotive held, out CTCSignal signal)
  {
    foreach (var engine in engines)
    {
      if (!engine.IsStopped(HeldSeconds)) continue;
      var waiting = WaitingSignal(new AutoEngineerPersistence(engine.KeyValueObject));
      if (waiting == null || waiting.Interlocking != interlocking) continue;
      var at = waiting.transform.GamePosition();
      if (!cars.Any(car => Vector3.Distance(car.GetCenterPosition(), at) <= car.carLength / 2f + HoldReach)) continue;
      held = engine;
      signal = waiting;
      return true;
    }
    held = null;
    signal = null;
    return false;
  }

  private static bool GoesFirst(BaseLocomotive held, CTCSignal heldSignal, List<Car> cars, BaseLocomotive locomotive)
  {
    if (string.CompareOrdinal(held.id, locomotive.id) >= 0) return false;
    var list = OrderList.Read(held);
    if (!list.Running || !list.PassSignals) return false;
    return BlocksClear(heldSignal, new HashSet<Car>(cars));
  }
}
