using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal sealed class RunAroundWatch : MonoBehaviour
{
  private const string TrainOrdersHookKey = "qbanrambo.train-orders.runaround";
  private const float TickSeconds = 0.5f;
  private const float StoppedSeconds = 1f;
  private const float CutTimeoutSeconds = 5f;
  private const float OrderSettleSeconds = 2f;
  private const float StallSeconds = 60f;
  private const float MovingSpeed = 0.3f;

  private static RunAroundWatch _shared;

  private readonly Dictionary<string, Job> _jobs = new();
  private float _nextTick;

  internal static List<Car> Group(BaseLocomotive locomotive, bool front)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var index = cars.IndexOf(locomotive);
    if (index < 0) return new List<Car>();
    var atB = front == (locomotive.EndToLogical(Car.End.F) == Car.LogicalEnd.B);
    var side = atB ? Enumerable.Reverse(cars.Skip(index + 1)) : cars.Take(index);
    return side.TakeWhile(IsCar).ToList();
  }

  internal static string Refusal(BaseLocomotive locomotive, bool front)
  {
    if (!locomotive.IsStopped(StoppedSeconds)) return "Stop the train first";
    return CanGoAround(locomotive, front, out _, out _, out _) ? null : "No siding or loop beside them";
  }

  internal static void Begin(BaseLocomotive locomotive, bool front)
  {
    if (Delivery.Captures(locomotive))
    {
      if (!AddToOrders(locomotive, front)) Toast.Present("This Train Orders can't take a run around from Go to: use Leave cars > Run around in its window", ToastPosition.Bottom);
      return;
    }
    if (!locomotive.IsStopped(StoppedSeconds))
    {
      Toast.Present($"Stop {locomotive.DisplayName} first", ToastPosition.Bottom);
      return;
    }
    if (!CanGoAround(locomotive, front, out var group, out var farEnd, out var problem))
    {
      Toast.Present($"{locomotive.DisplayName} can't run around the cars: {problem}", ToastPosition.Bottom);
      return;
    }
    if (!Delivery.Cut(locomotive, Delivery.Ids(group), out var cutCar))
    {
      Toast.Present($"{locomotive.DisplayName} couldn't uncouple the cars", ToastPosition.Bottom);
      return;
    }
    if (_shared == null) _shared = new GameObject("Goto Run Around Watch").AddComponent<RunAroundWatch>();
    _shared._jobs[locomotive.id] = new Job(locomotive, front, group[0], farEnd, cutCar);
    Toast.Present($"{locomotive.DisplayName} leaving {Delivery.Cars(group.Count)} to run around them", ToastPosition.Bottom);
  }

  internal static void Clear()
  {
    if (_shared != null)
    {
      _shared._jobs.Clear();
      Destroy(_shared.gameObject);
    }
    _shared = null;
  }

  private static bool CanGoAround(BaseLocomotive locomotive, bool front, out List<Car> group, out Car.End farEnd, out string problem)
  {
    group = Group(locomotive, front);
    farEnd = Car.End.F;
    problem = "No cars to go around";
    if (group.Count == 0) return false;
    var far = group[0];
    farEnd = RunAround.OuterEnd(group, far, locomotive);
    var leaving = new HashSet<Car>(group);
    var staying = locomotive.EnumerateCoupled().Where(car => !leaving.Contains(car)).ToList();
    return RunAround.CanGoAround(group, far, farEnd, AutoEngineerPlanner.CalculateTotalLength(staying), out problem);
  }

  private static bool AddToOrders(BaseLocomotive locomotive, bool front)
  {
    try
    {
      return AppDomain.CurrentDomain.GetData(TrainOrdersHookKey) is Func<object, bool, bool> hook && hook(locomotive, front);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Train Orders didn't take the run around: {exception.Message}");
      return false;
    }
  }

  private void OnDestroy()
  {
    if (_shared == this) _shared = null;
  }

  private void Update()
  {
    if (Time.unscaledTime < _nextTick) return;
    _nextTick = Time.unscaledTime + TickSeconds;
    foreach (var id in _jobs.Keys.ToList())
    {
      try
      {
        if (Check(_jobs[id])) _jobs.Remove(id);
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Error(exception, "Couldn't finish the run around");
        _jobs.Remove(id);
      }
    }
    if (_jobs.Count == 0) Destroy(gameObject);
  }

  private static bool Check(Job job)
  {
    var locomotive = job.Locomotive;
    if (locomotive == null || job.Far == null) return true;
    if (job.LocationString == null)
    {
      if (locomotive.EnumerateCoupled().Contains(job.CutCar))
      {
        if (Time.time - job.Started <= CutTimeoutSeconds) return false;
        Toast.Present($"{locomotive.DisplayName} couldn't uncouple the cars", ToastPosition.Bottom);
        return true;
      }
      return Send(job);
    }
    var orders = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders;
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed) return true;
    if (Time.time - job.Sent <= OrderSettleSeconds) return false;
    if (orders.Mode != AutoEngineerMode.Waypoint || orders.Waypoint?.LocationString != job.LocationString) return true;
    if (Time.time - job.Sent <= StallSeconds || !locomotive.IsStopped(StallSeconds / 2f)) return false;
    Toast.Present($"{locomotive.DisplayName} couldn't get around the cars: the engineer didn't move. The cars are cut, couple back to them", ToastPosition.Bottom);
    return true;
  }

  private static bool Send(Job job)
  {
    var locomotive = job.Locomotive;
    var order = CoupleAt(locomotive, job.Far, job.FarEnd, EndAwayFrom(locomotive, job.Front), out var problem);
    if (!order.HasValue)
    {
      Toast.Present($"{locomotive.DisplayName} couldn't get around the cars: {problem?.TrimEnd('.')}. The cars are cut, couple back to them", ToastPosition.Bottom);
      return true;
    }
    var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
    AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
    AppDomain.CurrentDomain.SetData(CoupleWatch.WatchedKey, true);
    try
    {
      helper.SetWaypointOrder(order.Value);
      if (AppDomain.CurrentDomain.GetData(GotoMenu.CapturedKey) is true) return true;
    }
    finally
    {
      AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
      AppDomain.CurrentDomain.SetData(CoupleWatch.WatchedKey, null);
    }
    CoupleWatch.Begin(locomotive, job.Far, order.Value.LocationString);
    job.LocationString = order.Value.LocationString;
    job.Sent = Time.time;
    var released = GotoSettings.ReleaseHandbrakes ? GotoMenu.ReleaseHandbrakes(locomotive) : 0;
    var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
    Toast.Present($"{locomotive.DisplayName} going around the cars to couple to {job.Far.DisplayName}{handbrakes}", ToastPosition.Bottom);
    return false;
  }

  private static OrderWaypoint? CoupleAt(BaseLocomotive locomotive, Car car, Car.End end, WaypointEnd arriving, out string problem)
  {
    problem = null;
    var view = Graph.Shared.Current;
    PlacementPlan plan;
    try
    {
      var mark = PosedWaypoint.CouplerMark(view, car, end);
      plan = PosedWaypoint.Plan(locomotive, view, mark, car.id, arriving, WaypointPose.ToBodyToward(PosedWaypoint.CouplerBodySide(mark)), null, end);
    }
    catch (Exception exception)
    {
      problem = exception.Message;
      return null;
    }
    if (plan.IsPlanned && plan.Waypoint.HasValue) return plan.Waypoint.Value;
    problem = string.IsNullOrEmpty(plan.Message) ? $"Can't couple to {car.DisplayName}" : plan.Message;
    return null;
  }

  private static WaypointEnd EndAwayFrom(BaseLocomotive locomotive, bool front)
  {
    var cab = locomotive.EndToLogical(Car.End.F) == Car.LogicalEnd.B ? WaypointEnd.B : WaypointEnd.A;
    var back = cab == WaypointEnd.A ? WaypointEnd.B : WaypointEnd.A;
    return front ? back : cab;
  }

  private static bool IsCar(Car car) => car is not BaseLocomotive && !car.CarType.IsTender;

  private sealed class Job
  {
    public Job(BaseLocomotive locomotive, bool front, Car far, Car.End farEnd, Car cutCar)
    {
      Locomotive = locomotive;
      Front = front;
      Far = far;
      FarEnd = farEnd;
      CutCar = cutCar;
      Started = Time.time;
    }

    public BaseLocomotive Locomotive { get; }
    public bool Front { get; }
    public Car Far { get; }
    public Car.End FarEnd { get; }
    public Car CutCar { get; }
    public float Started { get; }
    public string LocationString { get; set; }
    public float Sent { get; set; }
  }
}
