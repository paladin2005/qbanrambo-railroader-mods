using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Model.Ops;
using Network.Messages;
using Track;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal sealed class Dispatcher : MonoBehaviour
{
  private const float TickSeconds = 0.5f;
  private const float SettleSeconds = 1f;
  private const float CoupledSettleSeconds = 3f;
  private const float CutTimeoutSeconds = 5f;
  private const float RunAroundStallSeconds = 60f;
  private const float StoppedNearSeconds = 5f;
  private const float ArrivalTolerance = 15f;
  private const float MovingSpeed = 0.5f;
  private const double LoadStallGameSeconds = 2 * 60 * 60;
  private const int GoBackHorizonMinutes = 12 * 60;
  private const float LoadProgressEpsilon = 0.001f;
  private const string AlreadyPositioned = "Already positioned at waypoint.";

  private const float MinimumLapSeconds = 2f;
  private const float StationSettleSeconds = 2f;
  private const double MinimumBoardingGameSeconds = 15;
  private const double BoardingSettleGameSeconds = 8;

  private static readonly Dictionary<string, Runner> Runners = new();
  private static readonly HashSet<string> StationWarnings = new();
  private static readonly Dictionary<string, float> LapStarts = new();

  private readonly HashSet<string> _failing = new();
  private float _nextTick;

  internal static void OnWaypointNotice(BaseLocomotive locomotive, string message)
  {
    if (locomotive == null || message == null || !Runners.TryGetValue(locomotive.id, out var runner) || runner.Phase != Phase.Moving) return;
    if (message == AutoEngineerPlanner.ArrivedNotice) runner.Arrived = true;
    else if (message == AutoEngineerPlanner.PlacementMismatchNotice || message == AutoEngineerPlanner.CouplingMismatchNotice) runner.Problem = message;
  }

  internal static void OnRouteError(BaseLocomotive locomotive, string message)
  {
    if (locomotive == null || message == null || !Runners.TryGetValue(locomotive.id, out var runner)) return;
    if (runner.Phase is Phase.Refuel or Phase.Turntable)
    {
      if (!message.EndsWith(AlreadyPositioned)) runner.Problem ??= message;
      return;
    }
    if (runner.Phase != Phase.Moving) return;
    if (message.EndsWith(AlreadyPositioned)) runner.Arrived = true;
    else runner.Problem ??= message;
  }

  private static void DropRunner(string id)
  {
    if (!Runners.TryGetValue(id, out var runner)) return;
    runner.Fuel?.Release();
    Runners.Remove(id);
  }

  private void OnDestroy()
  {
    foreach (var id in Runners.Keys.ToList()) DropRunner(id);
    Runners.Clear();
    StationWarnings.Clear();
    LapStarts.Clear();
  }

  private static void KeepStations(BaseLocomotive locomotive, OrderList orders)
  {
    var stations = orders.Stations;
    if (stations.Count == 0) return;
    try
    {
      Passengers.Keep(locomotive, orders.StationsAhead, stations);
    }
    catch (Exception exception)
    {
      if (StationWarnings.Add(locomotive.id)) TrainOrdersMod.Log?.Error(exception, $"Couldn't keep the stations checked for {locomotive.DisplayName}");
    }
  }

  private void Update()
  {
    if (Time.unscaledTime < _nextTick) return;
    _nextTick = Time.unscaledTime + TickSeconds;
    if (!StateManager.IsHost) return;
    var controller = TrainController.Shared;
    if (controller == null) return;
    var seen = new HashSet<string>();
    foreach (var locomotive in controller.Cars.OfType<BaseLocomotive>().ToList())
    {
      if (locomotive == null) continue;
      seen.Add(locomotive.id);
      try
      {
        Tick(locomotive);
        _failing.Remove(locomotive.id);
      }
      catch (Exception exception)
      {
        if (_failing.Add(locomotive.id)) TrainOrdersMod.Log?.Error(exception, $"Train Orders failed for {locomotive.DisplayName}");
        DropRunner(locomotive.id);
        TryPause(locomotive, "Something went wrong, see the Message Log");
      }
    }
    foreach (var id in Runners.Keys.Where(id => !seen.Contains(id)).ToList()) DropRunner(id);
  }

  private static void Tick(BaseLocomotive locomotive)
  {
    var orders = OrderList.Read(locomotive);
    if (!orders.Running)
    {
      DropRunner(locomotive.id);
      return;
    }
    if (orders.Finished)
    {
      DropRunner(locomotive.id);
      orders.Finish();
      orders.Write(locomotive);
      return;
    }
    KeepStations(locomotive, orders);
    var step = orders.Steps[orders.Current];
    if (!Runners.TryGetValue(locomotive.id, out var runner) || runner.StepKey != step.Key)
    {
      DropRunner(locomotive.id);
      runner = new Runner(step.Key);
      Runners[locomotive.id] = runner;
    }
    if (orders.PassSignals && runner.Phase is Phase.Moving or Phase.Refuel) Signals.TryPass(locomotive);
    switch (runner.Phase)
    {
      case Phase.Idle:
        Start(locomotive, orders, step, runner);
        break;
      case Phase.Moving:
        if (step.Kind == StepKind.RunAround && !runner.Moved && Time.time - runner.SentAt > RunAroundStallSeconds && locomotive.IsStopped(RunAroundStallSeconds / 2f))
        {
          ClearLeftoverWaypoint(locomotive);
          Pause(locomotive, orders, "Couldn't get around the cars: the engineer didn't move. The cars are cut, couple back to them");
          return;
        }
        CheckMove(locomotive, orders, runner);
        break;
      case Phase.Arrived:
        if (!locomotive.IsStopped(SettleSeconds)) return;
        ClearLeftoverWaypoint(locomotive);
        if (runner.CoupleCarId != null)
        {
          Maneuvers.ConnectAir(locomotive);
          Maneuvers.ReleaseHandbrakes(locomotive);
          if ((step.Kind == StepKind.PickUp || (step.IsCouple && (step.Count != Maneuvers.AllCars || step.Leave.HasValue))) && TryGetCar(runner.CoupleCarId, out var picked))
          {
            Trim(locomotive, orders, step, runner, picked);
            return;
          }
        }
        else if (Passengers.AtAnyStation(locomotive))
        {
          StartBoarding(locomotive, runner);
          return;
        }
        if (step.Kind == StepKind.RunAround && orders.LeftCarId == step.CutCarId) orders.LeftCarId = null;
        Complete(locomotive, orders);
        break;
      case Phase.Boarding:
        CheckBoarding(locomotive, orders, runner);
        break;
      case Phase.Cutting:
        if (!locomotive.EnumerateCoupled().Contains(runner.CutCar))
        {
          if (step.Kind == StepKind.RunAround) runner.Phase = Phase.Idle;
          else Complete(locomotive, orders);
        }
        else if (Time.time - runner.CutStarted > CutTimeoutSeconds) Pause(locomotive, orders, "The cut didn't happen");
        break;
      case Phase.Loading:
        CheckLoading(locomotive, orders, runner);
        break;
      case Phase.Repairing:
        CheckRepair(locomotive, orders, runner);
        break;
      case Phase.Refuel:
        CheckRefuel(locomotive, orders, runner);
        break;
      case Phase.Turntable:
        CheckTurntable(locomotive, orders, runner);
        break;
    }
  }

  private static void StartPickUp(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    if (!locomotive.IsStopped(SettleSeconds)) return;
    Car target = null;
    if (step.CutCarId != null && TryGetCar(step.CutCarId, out var saved) && !saved.IsInBardo) target = saved;
    if (target != null && locomotive.EnumerateCoupled().Contains(target))
    {
      Trim(locomotive, orders, step, runner, target);
      return;
    }
    string problem;
    OrderWaypoint? order;
    if (target != null)
    {
      order = Pickups.CoupleTo(locomotive, target, out problem);
    }
    else
    {
      order = Pickups.Nearest(locomotive, Previous(orders), out target, out problem);
      if (order.HasValue && target != null)
      {
        orders.Steps[orders.Current] = step.WithCutCar(target.id);
        orders.Write(locomotive);
      }
    }
    if (!order.HasValue)
    {
      Pause(locomotive, orders, problem);
      return;
    }
    Send(locomotive, runner, order.Value);
  }

  private static void Trim(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner, Car target)
  {
    if (step.Leave.HasValue)
    {
      LeaveOwn(locomotive, orders, step, runner, target, step.Leave.Value);
      return;
    }
    var group = Pickups.GroupFrom(locomotive, target);
    if (step.Count == Maneuvers.AllCars || group.Count <= step.Count)
    {
      Complete(locomotive, orders);
      return;
    }
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var towardB = cars.IndexOf(target) > cars.IndexOf(locomotive);
    var plan = new Maneuvers.CutPlan(group[step.Count - 1], group[step.Count], towardB);
    orders.NoteLeft(step, plan.CutCar.id);
    orders.Write(locomotive);
    Maneuvers.ExecuteCut(plan);
    runner.CutCar = plan.CutCar;
    runner.CutStarted = Time.time;
    runner.Phase = Phase.Cutting;
  }

  private static void LeaveOwn(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner, Car target, int leave)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var targetIndex = cars.IndexOf(target);
    var locomotiveIndex = cars.IndexOf(locomotive);
    if (targetIndex < 0 || locomotiveIndex < 0)
    {
      Complete(locomotive, orders);
      return;
    }
    var towardB = targetIndex > locomotiveIndex;
    var inward = new List<Car>();
    for (var i = towardB ? targetIndex - 1 : targetIndex + 1; towardB ? i >= locomotiveIndex : i <= locomotiveIndex; i += towardB ? -1 : 1) inward.Add(cars[i]);
    var own = inward.TakeWhile(Maneuvers.IsCar).Take(leave).ToList();
    if (leave == OrderStep.LeaveAll && own.Count > 0) leave = own.Count;
    if (own.Count < leave || inward.Count <= leave)
    {
      Pause(locomotive, orders, own.Count == 0 ? "No cars of the train to leave next to the coupling" : $"Only {(own.Count == 1 ? "1 car" : $"{own.Count} cars")} of the train to leave next to the coupling");
      return;
    }
    var cut = leave == 0 ? target : own[leave - 1];
    var kept = inward[leave];
    var plan = new Maneuvers.CutPlan(kept, cut, towardB);
    orders.NoteLeft(step, plan.CutCar.id);
    orders.Write(locomotive);
    Maneuvers.ExecuteCut(plan);
    runner.CutCar = plan.CutCar;
    runner.CutStarted = Time.time;
    runner.Phase = Phase.Cutting;
  }

  private static void StartRepair(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    if (!locomotive.IsStopped(SettleSeconds)) return;
    var (repaired, total, sum) = Repairs.Measure(locomotive);
    if (repaired == total)
    {
      Complete(locomotive, orders);
      return;
    }
    if (!Repairs.OnRepairTrack(locomotive))
    {
      Pause(locomotive, orders, "Not on a repair track");
      return;
    }
    runner.LoadFill = sum;
    runner.LoadProgressAt = TimeWeather.Now.TotalSeconds;
    runner.Phase = Phase.Repairing;
  }

  private static void CheckRepair(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    var (repaired, total, sum) = Repairs.Measure(locomotive);
    if (repaired == total)
    {
      Complete(locomotive, orders);
      return;
    }
    var now = TimeWeather.Now.TotalSeconds;
    if (sum > runner.LoadFill + LoadProgressEpsilon)
    {
      runner.LoadFill = sum;
      runner.LoadProgressAt = now;
      return;
    }
    if (now - runner.LoadProgressAt <= LoadStallGameSeconds) return;
    var waiting = total - repaired;
    Pause(locomotive, orders, $"Repairs stopped with {waiting} {(waiting == 1 ? "car" : "cars")} not repaired");
  }

  private static void StartCoupleTo(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    var source = orders.IndexOfId(step.From);
    var which = source < 0 ? "the order that leaves them" : $"order {source + 1}";
    if (step.From == null || !orders.Cuts.TryGetValue(step.From, out var carId))
    {
      Pause(locomotive, orders, source < 0 ? "The order that leaves those cars is gone" : $"Order {source + 1} hasn't left its cars yet");
      return;
    }
    if (!TryGetCar(carId, out var car) || car.IsInBardo)
    {
      Pause(locomotive, orders, $"The cars left by {which} are gone");
      return;
    }
    if (locomotive.EnumerateCoupled().Contains(car))
    {
      Complete(locomotive, orders);
      return;
    }
    if (!locomotive.IsStopped(SettleSeconds)) return;
    var order = Pickups.CoupleTo(locomotive, car, out var problem);
    if (!order.HasValue)
    {
      Pause(locomotive, orders, problem);
      return;
    }
    Send(locomotive, runner, order.Value);
  }

  private static void StartCoupleBack(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    if (orders.LeftCarId == null || !TryGetCar(orders.LeftCarId, out var car) || car.IsInBardo)
    {
      Pause(locomotive, orders, "No cars left behind to couple back to");
      return;
    }
    if (locomotive.EnumerateCoupled().Contains(car))
    {
      Complete(locomotive, orders);
      return;
    }
    if (!locomotive.IsStopped(SettleSeconds)) return;
    var order = Pickups.CoupleTo(locomotive, car, out var problem);
    if (!order.HasValue)
    {
      Pause(locomotive, orders, problem);
      return;
    }
    Send(locomotive, runner, order.Value);
  }

  private static OrderStep Previous(OrderList orders)
  {
    if (orders.Current > 0) return orders.Steps[orders.Current - 1];
    return orders.Repeat && orders.Steps.Count > 1 ? orders.Steps[orders.Steps.Count - 1] : null;
  }

  private static bool TryGetCar(string id, out Car car)
  {
    car = null;
    return !string.IsNullOrEmpty(id) && TrainController.Shared.TryGetCarForId(id, out car) && car != null;
  }

  private static void StartTurntable(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    var site = Turntables.Find(step.TurntableId);
    if (site == null)
    {
      Pause(locomotive, orders, "The turntable isn't available");
      return;
    }
    var refusal = TurntableRun.Refusal(locomotive, site);
    if (refusal != null)
    {
      Pause(locomotive, orders, refusal);
      return;
    }
    PitTrack goal = null;
    if (step.Exit.HasValue)
    {
      goal = site.Tracks.FirstOrDefault(track => track.Index == step.Exit.Value);
      if (goal == null)
      {
        Pause(locomotive, orders, $"That track is gone from the {site.Name}");
        return;
      }
    }
    var key = step.Key;
    runner.Turntable = new TurntableRun(locomotive, site, goal, step.TurntableState, state =>
    {
      var current = OrderList.Read(locomotive);
      if (current.Finished || current.Steps[current.Current].Key != key) return;
      current.Steps[current.Current] = current.Steps[current.Current].WithTurntableState(state);
      current.Write(locomotive);
    });
    runner.Phase = Phase.Turntable;
    runner.Turntable.Begin();
    CheckTurntable(locomotive, orders, runner);
  }

  private static void CheckTurntable(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    var run = runner.Turntable;
    if (runner.Problem != null)
    {
      Pause(locomotive, orders, runner.Problem);
      return;
    }
    if (run.Problem == null && !run.Done) run.Tick();
    if (run.Problem != null) Pause(locomotive, orders, run.Problem);
    else if (run.Done) Complete(locomotive, orders);
  }

  internal static string TurntableDetail(Car locomotive) => locomotive != null && Runners.TryGetValue(locomotive.id, out var runner) && runner.Phase == Phase.Turntable ? runner.Turntable?.Detail : null;

  private static void StartRefuel(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    var fuelCar = Fuel.FuelCar(locomotive);
    if (fuelCar == null)
    {
      Pause(locomotive, orders, "No tender coupled");
      return;
    }
    var stops = step.FuelIds.Select(Fuel.Find).Where(stop => stop != null).ToList();
    if (stops.Count == 0)
    {
      Pause(locomotive, orders, "The fuel loader is gone");
      return;
    }
    var usable = stops.Where(stop => Fuel.Slot(fuelCar, stop.Load) != null).ToList();
    if (usable.Count == 0)
    {
      Pause(locomotive, orders, $"{locomotive.DisplayName} takes none of that fuel");
      return;
    }
    if (!Fuel.NeedsFuel(fuelCar, usable, step.Count))
    {
      TrainOrdersMod.Log?.Info($"Refuel skipped for {locomotive.DisplayName}: {Fuel.Levels(fuelCar)}, threshold {step.Count}%");
      Complete(locomotive, orders);
      return;
    }
    runner.Fuel = new FuelRun(locomotive, usable);
    runner.Phase = Phase.Refuel;
    runner.Fuel.Begin();
    CheckRefuel(locomotive, orders, runner);
  }

  private static void CheckRefuel(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    var run = runner.Fuel;
    if (runner.Problem != null)
    {
      Pause(locomotive, orders, runner.Problem);
      return;
    }
    if (run.Problem == null && !run.Done) run.Tick();
    if (run.Problem != null) Pause(locomotive, orders, run.Problem);
    else if (run.Done) Complete(locomotive, orders);
  }

  internal static string RefuelDetail(Car locomotive) => locomotive != null && Runners.TryGetValue(locomotive.id, out var runner) && runner.Phase == Phase.Refuel ? runner.Fuel?.Detail : null;

  internal static bool IsBoarding(Car locomotive) => locomotive != null && Runners.TryGetValue(locomotive.id, out var runner) && runner.Phase == Phase.Boarding;

  private static void StartBoarding(BaseLocomotive locomotive, Runner runner)
  {
    var now = TimeWeather.Now.TotalSeconds;
    runner.Phase = Phase.Boarding;
    runner.BoardingSince = now;
    runner.BoardingChangedAt = now;
    runner.BoardingSignature = Passengers.Signature(locomotive);
  }

  private static void CheckBoarding(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    var now = TimeWeather.Now.TotalSeconds;
    var signature = Passengers.Signature(locomotive);
    if (signature != runner.BoardingSignature)
    {
      runner.BoardingSignature = signature;
      runner.BoardingChangedAt = now;
    }
    if (!locomotive.IsStopped(SettleSeconds)) return;
    if (now - runner.BoardingSince < Math.Max(Passengers.MinimumStopSeconds, MinimumBoardingGameSeconds)) return;
    if (now - runner.BoardingChangedAt < BoardingSettleGameSeconds) return;
    Complete(locomotive, orders);
  }

  private static void CheckLoading(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    var load = Loads.Measure(locomotive);
    if (runner.Unloading ? load.AllEmpty : load.AllFull)
    {
      Complete(locomotive, orders);
      return;
    }
    var now = TimeWeather.Now.TotalSeconds;
    var progressed = runner.Unloading ? load.Fill < runner.LoadFill - LoadProgressEpsilon : load.Fill > runner.LoadFill + LoadProgressEpsilon;
    if (progressed)
    {
      runner.LoadFill = load.Fill;
      runner.LoadProgressAt = now;
    }
    else if (now - runner.LoadProgressAt > LoadStallGameSeconds)
    {
      var waiting = runner.Unloading ? load.Total - load.Empty : load.Total - load.Full;
      var cars = waiting == 1 ? "car" : "cars";
      Pause(locomotive, orders, runner.Unloading ? $"Unloading stopped with {waiting} {cars} not empty" : $"Loading stopped with {waiting} {cars} not full");
    }
  }

  private static void Start(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    switch (step.Kind)
    {
      case StepKind.Move:
        StartMove(locomotive, orders, step, runner);
        break;
      case StepKind.Cut:
        if (step.CutCarId != null && !locomotive.EnumerateCoupled().Any(car => car.id == step.CutCarId))
        {
          Complete(locomotive, orders);
          return;
        }
        if (!locomotive.IsStopped(SettleSeconds)) return;
        Maneuvers.CutPlan plan;
        string problem;
        var planned = step.Keep ? Maneuvers.PlanKeep(locomotive, step.Count, step.Side, out plan, out problem) : Maneuvers.PlanCut(locomotive, step.Count, step.Side, out plan, out problem);
        if (!planned)
        {
          Pause(locomotive, orders, problem);
          return;
        }
        orders.Steps[orders.Current] = step.WithCutCar(plan.CutCar.id);
        orders.NoteLeft(step, plan.CutCar.id);
        orders.Write(locomotive);
        Maneuvers.ExecuteCut(plan);
        runner.CutCar = plan.CutCar;
        runner.CutStarted = Time.time;
        runner.Phase = Phase.Cutting;
        break;
      case StepKind.WaitLoad:
      case StepKind.WaitUnload:
        if (!locomotive.IsStopped(SettleSeconds)) return;
        var load = Loads.Measure(locomotive);
        runner.Unloading = step.Kind == StepKind.WaitUnload;
        if (load.Total == 0 || (runner.Unloading ? load.AllEmpty : load.AllFull))
        {
          Complete(locomotive, orders);
          return;
        }
        runner.LoadFill = load.Fill;
        runner.LoadProgressAt = TimeWeather.Now.TotalSeconds;
        runner.Phase = Phase.Loading;
        break;
      case StepKind.WaitRepair:
        StartRepair(locomotive, orders, runner);
        break;
      case StepKind.WaitDay:
      case StepKind.WaitHours:
      case StepKind.WaitMinutes:
      case StepKind.WaitTime:
        var now = TimeWeather.Now;
        if (!step.UntilMinute.HasValue)
        {
          orders.Steps[orders.Current] = step.WithUntil(step.UntilFrom(now));
          orders.Write(locomotive);
        }
        else if (OrderStep.Minute(now) >= step.UntilMinute.Value)
        {
          Complete(locomotive, orders);
        }
        break;
      case StepKind.Station:
        StartStation(locomotive, orders, step, runner);
        break;
      case StepKind.Refuel:
        StartRefuel(locomotive, orders, step, runner);
        break;
      case StepKind.Turntable:
        StartTurntable(locomotive, orders, step, runner);
        break;
      case StepKind.PickUp:
        StartPickUp(locomotive, orders, step, runner);
        break;
      case StepKind.CoupleBack:
        StartCoupleBack(locomotive, orders, runner);
        break;
      case StepKind.CoupleTo:
        StartCoupleTo(locomotive, orders, step, runner);
        break;
      case StepKind.RunAround:
        StartRunAround(locomotive, orders, step, runner);
        break;
      case StepKind.SetSpeed:
        StartSetSpeed(locomotive, orders, step);
        break;
      case StepKind.GoBack:
        StartGoBack(locomotive, orders, step);
        break;
    }
  }

  private static void StartGoBack(BaseLocomotive locomotive, OrderList orders, OrderStep step)
  {
    var target = orders.IndexOfId(step.From);
    if (target < 0 || target >= orders.Current)
    {
      Pause(locomotive, orders, "The order to go back to is gone");
      return;
    }
    if (step.Times.HasValue)
    {
      var laps = step.Laps ?? step.Times.Value;
      if (laps <= 0)
      {
        Complete(locomotive, orders);
        return;
      }
      orders.Steps[orders.Current] = step.WithLaps(laps - 1);
    }
    else
    {
      var now = OrderStep.Minute(TimeWeather.Now);
      var until = step.UntilMinute ?? step.UntilFrom(TimeWeather.Now);
      if (now >= until || until - now > GoBackHorizonMinutes)
      {
        Complete(locomotive, orders);
        return;
      }
      if (!step.UntilMinute.HasValue) orders.Steps[orders.Current] = step.WithUntil(until);
    }
    if (LapTooShort(locomotive))
    {
      Pause(locomotive, orders, "Nothing to repeat: the orders finish straight away");
      return;
    }
    DropRunner(locomotive.id);
    orders.JumpTo(target);
    orders.Write(locomotive);
    Toast.Present($"{locomotive.DisplayName}: back to order {target + 1}, {OrderActions.Describe(orders.Steps[target])}", ToastPosition.Bottom);
  }

  private static void StartSetSpeed(BaseLocomotive locomotive, OrderList orders, OrderStep step)
  {
    orders.Speed = step.Count;
    var current = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders;
    if (current.Mode == AutoEngineerMode.Waypoint) Helper(locomotive).SetOrdersValue(maxSpeedMph: Mph(step.Count));
    Complete(locomotive, orders);
  }

  internal static int? SpeedFor(Car locomotive)
  {
    var speed = OrderList.Read(locomotive).Speed;
    return speed == 0 ? null : Mph(speed);
  }

  internal static int Mph(int speed) => speed < 0 ? TrackSegment.RailroadSpeedLimitMph : speed;

  private static void StartRunAround(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    if (!locomotive.IsStopped(SettleSeconds)) return;
    var coupled = locomotive.EnumerateCoupled().ToList();
    if (step.FarId != null && TryGetCar(step.FarId, out var far) && !far.IsInBardo)
    {
      if (!coupled.Contains(far))
      {
        var farEnd = step.FarEnd ?? RunAround.OuterEnd(far.EnumerateCoupled().ToList(), far, locomotive);
        var order = Pickups.CoupleAt(locomotive, far, farEnd, Maneuvers.EndAwayFrom(locomotive, step.Side), out var couple);
        if (!order.HasValue)
        {
          Pause(locomotive, orders, $"Couldn't get around the cars: {couple?.TrimEnd('.')}. The cars are cut, couple back to them");
          return;
        }
        Send(locomotive, runner, order.Value);
        return;
      }
      if (Maneuvers.SideOf(locomotive, far) != step.Side)
      {
        if (orders.LeftCarId == step.CutCarId) orders.LeftCarId = null;
        Complete(locomotive, orders);
        return;
      }
    }
    if (!Maneuvers.PlanCut(locomotive, Maneuvers.AllCars, step.Side, out var plan, out var problem))
    {
      Pause(locomotive, orders, problem);
      return;
    }
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var farCar = plan.Far ?? plan.CutCar;
    var cutIndex = cars.IndexOf(plan.CutCar);
    var farIndex = cars.IndexOf(farCar);
    var leaving = cars.GetRange(Mathf.Min(cutIndex, farIndex), Mathf.Abs(cutIndex - farIndex) + 1);
    var staying = cars.Where(car => !leaving.Contains(car)).ToList();
    var outer = RunAround.OuterEnd(leaving, farCar, locomotive);
    if (!RunAround.CanGoAround(leaving, farCar, outer, AutoEngineerPlanner.CalculateTotalLength(staying), out var around))
    {
      Pause(locomotive, orders, $"No way around the cars: {around}");
      return;
    }
    orders.Steps[orders.Current] = step.WithRunAround(plan.CutCar.id, farCar.id, outer);
    orders.LeftCarId = plan.CutCar.id;
    orders.Write(locomotive);
    Maneuvers.ExecuteCut(plan);
    runner.CutCar = plan.CutCar;
    runner.CutStarted = Time.time;
    runner.Phase = Phase.Cutting;
  }

  private static void StartStation(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    var stop = Passengers.Stop(step.StationId);
    if (stop == null)
    {
      Pause(locomotive, orders, "The station is gone");
      return;
    }
    if (Passengers.Coaches(locomotive).Count > 0 && !Passengers.StopsEnabled)
    {
      Pause(locomotive, orders, "Turn on Passenger Stops in Company > Settings");
      return;
    }
    var spans = StationTracks.Chosen(stop, step.Track);
    if (locomotive.IsStopped(SettleSeconds) && Passengers.IsOn(spans, locomotive.EnumerateCoupled().ToList()))
    {
      runner.Phase = Phase.Arrived;
      return;
    }
    var target = Passengers.Target(locomotive, stop, spans, !string.IsNullOrEmpty(step.Track), out var failure);
    if (!target.HasValue && !string.IsNullOrEmpty(step.Track))
    {
      target = Passengers.Target(locomotive, stop, out var other);
      if (target.HasValue) Toast.Present($"{locomotive.DisplayName} can't reach the {StationTracks.Label(stop, step.Track).ToLowerInvariant()} at {stop.DisplayName}, taking the shortest route instead", ToastPosition.Bottom);
      else failure ??= other;
    }
    if (!target.HasValue)
    {
      Pause(locomotive, orders, failure ?? $"No route to {stop.DisplayName}");
      return;
    }
    runner.Station = stop;
    if (!string.IsNullOrEmpty(step.Track)) TrainOrdersMod.Log?.Info($"{locomotive.DisplayName} to {stop.DisplayName}, {StationTracks.Label(stop, step.Track)} ({step.Track}): waypoint {Graph.LocationToString(target.Value)}");
    Send(locomotive, runner, new OrderWaypoint(Graph.LocationToString(target.Value), null));
  }

  private static bool AtStation(BaseLocomotive locomotive, PassengerStop stop)
  {
    if (!locomotive.IsStopped(StationSettleSeconds)) return false;
    var coaches = Passengers.Coaches(locomotive);
    return coaches.Count > 0 && Passengers.IsOn(stop, coaches);
  }

  private static void StartMove(BaseLocomotive locomotive, OrderList orders, OrderStep step, Runner runner)
  {
    if (!TryBuildOrder(locomotive, step, out var order, out var problem))
    {
      Pause(locomotive, orders, problem);
      return;
    }
    Send(locomotive, runner, order);
  }

  private static void Send(BaseLocomotive locomotive, Runner runner, OrderWaypoint order)
  {
    Maneuvers.ReleaseHandbrakes(locomotive);
    runner.SentLocation = order.LocationString;
    runner.CoupleCarId = order.WantsCouple ? order.CoupleToCarId : null;
    runner.Phase = Phase.Moving;
    runner.SentAt = Time.time;
    runner.Arrived = false;
    runner.Moved = false;
    runner.Problem = null;
    OrderCapture.Dispatching = true;
    try
    {
      Helper(locomotive).SetWaypointOrder(order, SpeedFor(locomotive));
    }
    finally
    {
      OrderCapture.Dispatching = false;
    }
  }

  private static bool TryBuildOrder(BaseLocomotive locomotive, OrderStep step, out OrderWaypoint order, out string problem)
  {
    order = default;
    problem = null;
    var intent = step.Waypoint.Value;
    var graph = Graph.Shared;
    if (intent.WantsCouple)
    {
      if (!TrainController.Shared.TryGetCarForId(intent.CoupleToCarId, out var car) || car == null)
      {
        problem = "The car to couple to is gone";
        return false;
      }
      if (intent.CoupleToCarEnd.HasValue)
      {
        order = PosedWaypoint.CoupleOrder(locomotive, graph.Current, car, intent.CoupleToCarEnd.Value, step.ArrivingEndFor(locomotive));
        return true;
      }
    }
    Location mark;
    try
    {
      mark = graph.ResolveLocationString(intent.LocationString);
    }
    catch (Exception)
    {
      problem = "The waypoint's track is gone";
      return false;
    }
    if (step.Smart == true)
    {
      if (DeadEnds.TrySmartWaypoint(locomotive, mark, out var smartOrder, out var smartProblem))
      {
        order = smartOrder;
        return true;
      }
      if (smartProblem != null) Toast.Present($"{locomotive.DisplayName} couldn't push the cars in: {smartProblem}", ToastPosition.Bottom);
    }
    order = PosedWaypoint.Order(locomotive, graph.Current, mark, step.ArrivingEndFor(locomotive), intent.BodyToward, null, intent.CoupleToCarId, intent.CoupleToCarEnd);
    return true;
  }

  private static void CheckMove(BaseLocomotive locomotive, OrderList orders, Runner runner)
  {
    if (runner.Arrived || (runner.Station != null && runner.Moved && AtStation(locomotive, runner.Station)))
    {
      runner.Phase = Phase.Arrived;
      return;
    }
    if (runner.Problem != null)
    {
      Pause(locomotive, orders, runner.Problem);
      return;
    }
    var current = Helper(locomotive).Orders;
    if (current.Mode != AutoEngineerMode.Waypoint)
    {
      Pause(locomotive, orders, "The engineer's orders were changed");
      return;
    }
    if (!current.Waypoint.HasValue)
    {
      Pause(locomotive, orders, "The waypoint was cleared");
      return;
    }
    var waypoint = current.Waypoint.Value;
    if (waypoint.LocationString != runner.SentLocation || (waypoint.WantsCouple ? waypoint.CoupleToCarId : null) != runner.CoupleCarId)
    {
      Pause(locomotive, orders, "Another waypoint was set");
      return;
    }
    if (runner.CoupleCarId != null && locomotive.IsStopped(CoupledSettleSeconds) && locomotive.EnumerateCoupled().Any(car => car.id == runner.CoupleCarId))
    {
      runner.Phase = Phase.Arrived;
      return;
    }
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed) runner.Moved = true;
    if (runner.CoupleCarId == null && runner.Moved && locomotive.IsStopped(StoppedNearSeconds) && IsNear(locomotive, runner.SentLocation)) runner.Phase = Phase.Arrived;
  }

  private static bool IsNear(BaseLocomotive locomotive, string locationString)
  {
    var graph = Graph.Shared;
    Vector3 target;
    try
    {
      target = graph.GetPosition(graph.ResolveLocationString(locationString));
    }
    catch (Exception)
    {
      return false;
    }
    return locomotive.EnumerateCoupled().Any(car => Vector3.Distance(car.GetCenterPosition(), target) <= car.carLength / 2f + ArrivalTolerance);
  }

  private static void ClearLeftoverWaypoint(BaseLocomotive locomotive)
  {
    var helper = Helper(locomotive);
    var current = helper.Orders;
    if (current.Mode == AutoEngineerMode.Waypoint && current.Waypoint.HasValue) helper.ClearWaypoint();
  }

  private static void Complete(BaseLocomotive locomotive, OrderList orders)
  {
    DropRunner(locomotive.id);
    if (orders.Advance())
    {
      orders.Finish();
      Toast.Present($"{locomotive.DisplayName}: all orders done", ToastPosition.Bottom);
    }
    else if (orders.Repeat && orders.Current == 0 && LapTooShort(locomotive))
    {
      Pause(locomotive, orders, "Nothing to repeat: the orders finish straight away");
      return;
    }
    else
    {
      Toast.Present($"{locomotive.DisplayName}: order done, next: {OrderActions.Describe(orders.Steps[orders.Current])}", ToastPosition.Bottom);
    }
    orders.Write(locomotive);
  }

  private static bool LapTooShort(BaseLocomotive locomotive)
  {
    var now = Time.unscaledTime;
    var tooShort = LapStarts.TryGetValue(locomotive.id, out var started) && now - started < MinimumLapSeconds;
    LapStarts[locomotive.id] = now;
    return tooShort;
  }

  private static void Pause(BaseLocomotive locomotive, OrderList orders, string reason)
  {
    DropRunner(locomotive.id);
    orders.Running = false;
    orders.Status = reason;
    orders.Write(locomotive);
    Toast.Present($"{locomotive.DisplayName} orders paused: {reason}", ToastPosition.Bottom);
  }

  private static void TryPause(BaseLocomotive locomotive, string reason)
  {
    try
    {
      Pause(locomotive, OrderList.Read(locomotive), reason);
    }
    catch (Exception)
    {
    }
  }

  private static AutoEngineerOrdersHelper Helper(BaseLocomotive locomotive) => new(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));

  private enum Phase
  {
    Idle,
    Moving,
    Arrived,
    Cutting,
    Loading,
    Boarding,
    Refuel,
    Turntable,
    Repairing
  }

  private sealed class Runner
  {
    public Runner(string stepKey) => StepKey = stepKey;

    public string StepKey { get; }
    public Phase Phase { get; set; }
    public string SentLocation { get; set; }
    public string CoupleCarId { get; set; }
    public float SentAt { get; set; }
    public bool Arrived { get; set; }
    public bool Moved { get; set; }
    public string Problem { get; set; }
    public Car CutCar { get; set; }
    public float CutStarted { get; set; }
    public float LoadFill { get; set; }
    public bool Unloading { get; set; }
    public double LoadProgressAt { get; set; }
    public PassengerStop Station { get; set; }
    public double BoardingSince { get; set; }
    public double BoardingChangedAt { get; set; }
    public string BoardingSignature { get; set; }
    public FuelRun Fuel { get; set; }
    public TurntableRun Turntable { get; set; }
  }
}

[HarmonyPatch(typeof(AutoEngineerPlanner), "PostWaypointNotice")]
internal static class WaypointNoticePatch
{
  private static void Postfix(string message, BaseLocomotive ____locomotive)
  {
    try
    {
      Dispatcher.OnWaypointNotice(____locomotive, message);
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't read the engineer's waypoint notice");
    }
  }
}

[HarmonyPatch(typeof(AutoEngineerPlanner), "SendMessageToRouteRequester")]
internal static class RouteMessagePatch
{
  private static void Prefix(string message, AlertLevel alertLevel, BaseLocomotive ____locomotive)
  {
    try
    {
      if (alertLevel == AlertLevel.Error) Dispatcher.OnRouteError(____locomotive, message);
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't read the engineer's route message");
    }
  }
}
