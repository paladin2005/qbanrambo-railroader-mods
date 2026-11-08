using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Model.Ops;
using Track;
using Track.Search;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal static class Delivery
{
  internal const string DeliverKey = "qbanrambo.goto.deliver";
  private const string TrainOrdersOpenKey = "qbanrambo.train-orders.window-open";
  private const float PullDistance = 3.5f;

  internal static string KeyFor(OpsCarPosition position)
  {
    var spans = (position.Spans ?? Array.Empty<TrackSpan>()).Where(span => span != null).Select(GotoMenu.SpanKey).OrderBy(key => key).ToList();
    return spans.Count > 0 ? string.Join("|", spans) : "id:" + position.Identifier;
  }

  internal static bool GoesTo(Car car, string key)
  {
    return car.Waybill.HasValue && !car.Waybill.Value.Completed && KeyFor(car.Waybill.Value.Destination) == key;
  }

  internal static List<Car> Block(BaseLocomotive locomotive, OpsCarPosition position, out bool atA)
  {
    atA = false;
    var key = KeyFor(position);
    return EndBlock(locomotive, car => GoesTo(car, key), out atA);
  }

  private static List<Car> EndBlock(BaseLocomotive locomotive, Func<Car, bool> member, out bool atA)
  {
    atA = false;
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var total = cars.Count(member);
    if (total == 0 || total >= cars.Count) return null;
    var fromA = cars.TakeWhile(member).ToList();
    if (fromA.Count == total && fromA.All(IsCar))
    {
      atA = true;
      return fromA;
    }
    var fromB = Enumerable.Reverse(cars).TakeWhile(member).ToList();
    return fromB.Count == total && fromB.All(IsCar) ? fromB : null;
  }

  internal static int Count(BaseLocomotive locomotive, OpsCarPosition position)
  {
    var key = KeyFor(position);
    return locomotive.EnumerateCoupled().Count(car => GoesTo(car, key));
  }

  internal static void Send(BaseLocomotive locomotive, OpsCarPosition position)
  {
    try
    {
      var block = Block(locomotive, position, out var atA);
      if (block == null)
      {
        Toast.Present($"The cars for {position.DisplayName} aren't together at one end of the train", ToastPosition.Bottom);
        return;
      }
      if (locomotive.IsStopped(1f) && OnSpans(block[0], position) && !Captures(locomotive))
      {
        if (Cut(locomotive, Ids(block), out var cutCar)) DeliverWatch.Cutting(locomotive, position, cutCar, block.Count);
        return;
      }
      var target = GotoMenu.PickTarget(locomotive, position, out var failure);
      if (target == null)
      {
        Toast.Present(failure ?? $"{locomotive.DisplayName} can't find a route to {position.DisplayName}.", ToastPosition.Bottom);
        return;
      }
      var body = BodySide(target.Value, OtherEnd(position, target.Value));
      var plan = PosedWaypoint.Plan(locomotive, Graph.Shared.Current, target.Value, null, atA ? WaypointEnd.A : WaypointEnd.B, WaypointPose.ToBodyToward(body));
      if (!plan.IsPlanned || !plan.Waypoint.HasValue)
      {
        var reason = string.IsNullOrEmpty(plan.Message) ? "" : $": {plan.Message}";
        Toast.Present($"{locomotive.DisplayName} can't bring the cars for {position.DisplayName} onto its track{reason}", ToastPosition.Bottom);
        return;
      }
      var waypoint = plan.Waypoint.Value;
      var front = locomotive.EndToLogical(Car.End.F) == (atA ? Car.LogicalEnd.A : Car.LogicalEnd.B);
      var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
      AppDomain.CurrentDomain.SetData(DeliverKey, $"{block.Count},{(front ? "front" : "rear")}");
      AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
      bool captured;
      bool cutQueued;
      try
      {
        helper.SetWaypointOrder(waypoint);
        captured = AppDomain.CurrentDomain.GetData(GotoMenu.CapturedKey) is true;
        cutQueued = AppDomain.CurrentDomain.GetData(DeliverKey) == null;
      }
      finally
      {
        AppDomain.CurrentDomain.SetData(DeliverKey, null);
        AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
      }
      if (captured)
      {
        if (!cutQueued) Toast.Present("Train Orders added the trip only; update Train Orders to queue the cut too", ToastPosition.Bottom);
        return;
      }
      DeliverWatch.Begin(locomotive, position, waypoint.LocationString, target.Value, Ids(block));
      var released = GotoSettings.ReleaseHandbrakes ? GotoMenu.ReleaseHandbrakes(locomotive) : 0;
      var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
      Toast.Present($"{locomotive.DisplayName} heading to leave {Cars(block.Count)} at {position.DisplayName}{handbrakes}", ToastPosition.Bottom);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Error(exception, $"Delivery to {position.Identifier} failed");
      Toast.Present("Go to failed. See the Message Log.", ToastPosition.Bottom);
    }
  }

  internal static HashSet<string> Ids(IEnumerable<Car> cars) => new(cars.Select(car => car.id));

  internal static bool Cut(BaseLocomotive locomotive, HashSet<string> ids, out Car cutCar)
  {
    cutCar = null;
    var block = EndBlock(locomotive, car => ids.Contains(car.id), out var atA);
    if (block == null) return false;
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    cutCar = block[block.Count - 1];
    var index = cars.IndexOf(cutCar);
    var keptIndex = atA ? index + 1 : index - 1;
    if (index < 0 || keptIndex < 0 || keptIndex >= cars.Count) return false;
    var kept = cars[keptIndex];
    var keptEnd = atA ? Car.LogicalEnd.A : Car.LogicalEnd.B;
    var cutEnd = atA ? Car.LogicalEnd.B : Car.LogicalEnd.A;
    if (cutCar.air != null && !cutCar.air.handbrakeApplied) cutCar.SetHandbrake(true);
    Coupling.SetAnglecock(kept, keptEnd, false);
    Coupling.SetAnglecock(cutCar, cutEnd, false);
    if (kept[keptEnd].IsAirConnected)
    {
      var first = atA ? cutCar : kept;
      var second = atA ? kept : cutCar;
      StateManager.ApplyLocal(new SetGladhandsConnected(first.id, second.id, false));
    }
    kept.ApplyEndGearChange(keptEnd, Car.EndGearStateKey.CutLever, 1f);
    return true;
  }

  internal static string Cars(int count) => count == 1 ? "1 car" : $"{count} cars";

  private static bool OnSpans(Car car, OpsCarPosition position)
  {
    var controller = TrainController.Shared;
    if (controller == null) return false;
    var buffer = new List<Car>();
    foreach (var span in position.Spans ?? Array.Empty<TrackSpan>())
    {
      if (span == null) continue;
      buffer.Clear();
      controller.GetCarsOnSpan(span, buffer);
      if (buffer.Contains(car)) return true;
    }
    return false;
  }

  internal static bool PullAway(BaseLocomotive locomotive, Car leftCar)
  {
    try
    {
      if (leftCar == null || Captures(locomotive)) return false;
      var graph = Graph.Shared;
      var view = graph.Current;
      var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
      if (cars.Count == 0) return false;
      var left = leftCar.GetCenterPosition();
      var first = cars[0];
      var last = cars[cars.Count - 1];
      var atA = Vector3.Distance(graph.GetPosition(first.LocationFor(Car.LogicalEnd.A)), left) > Vector3.Distance(graph.GetPosition(last.LocationFor(Car.LogicalEnd.B)), left);
      var car = atA ? first : last;
      var mark = PosedWaypoint.CouplerMark(view, car, car.LogicalToEnd(atA ? Car.LogicalEnd.A : Car.LogicalEnd.B));
      var target = view.LocationByMoving(mark, PullDistance, false, true);
      var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
      helper.SetWaypointOrder(new OrderWaypoint(Graph.LocationToString(target), null));
      return true;
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"{locomotive.DisplayName} couldn't pull away from the cars it left: {exception.Message}");
      return false;
    }
  }

  internal static bool Captures(BaseLocomotive locomotive)
  {
    return AppDomain.CurrentDomain.GetData(TrainOrdersOpenKey) is Func<object, bool> open && open(locomotive);
  }

  private static Location? OtherEnd(OpsCarPosition position, Location target)
  {
    foreach (var span in position.Spans ?? Array.Empty<TrackSpan>())
    {
      if (span == null) continue;
      if (span.lower.HasValue && span.lower.Value.Equals(target)) return span.upper;
      if (span.upper.HasValue && span.upper.Value.Equals(target)) return span.lower;
    }
    return null;
  }

  private static TrackSegment.End BodySide(Location target, Location? other)
  {
    var segment = target.segment;
    if (!other.HasValue) return TrackSegment.End.B;
    if (other.Value.segment == segment)
    {
      var length = segment.GetLength();
      float FromA(Location location) => location.EndIsA ? location.distance : length - location.distance;
      return FromA(other.Value) >= FromA(target) ? TrackSegment.End.B : TrackSegment.End.A;
    }
    var toward = Graph.Shared.GetPosition(other.Value);
    var nodeA = segment.NodeForEnd(TrackSegment.End.A);
    var nodeB = segment.NodeForEnd(TrackSegment.End.B);
    if (nodeA == null || nodeB == null) return TrackSegment.End.B;
    return Vector3.Distance(nodeA.Position, toward) <= Vector3.Distance(nodeB.Position, toward) ? TrackSegment.End.A : TrackSegment.End.B;
  }

  private static bool IsCar(Car car) => car is not BaseLocomotive && !car.CarType.IsTender;
}

internal sealed class DeliverWatch : MonoBehaviour
{
  private const float TickSeconds = 0.5f;
  private const float OrderSettleSeconds = 2f;
  private const float StoppedSeconds = 3f;
  private const float MovingSpeed = 0.3f;
  private const float ArrivalTolerance = 15f;
  private const float CutTimeoutSeconds = 5f;
  private const float TimeoutSeconds = 1800f;

  private static DeliverWatch _shared;

  private readonly Dictionary<string, Job> _jobs = new();
  private float _nextTick;

  internal static void Begin(BaseLocomotive locomotive, OpsCarPosition position, string locationString, Location target, HashSet<string> ids)
  {
    if (_shared == null) _shared = new GameObject("Goto Deliver Watch").AddComponent<DeliverWatch>();
    _shared._jobs[locomotive.id] = new Job(locomotive, ids, position.DisplayName, locationString, Graph.Shared.GetPosition(target), ids.Count);
  }

  internal static void Cutting(BaseLocomotive locomotive, OpsCarPosition position, Car cutCar, int count)
  {
    if (_shared == null) _shared = new GameObject("Goto Deliver Watch").AddComponent<DeliverWatch>();
    _shared._jobs[locomotive.id] = new Job(locomotive, new HashSet<string>(), position.DisplayName, null, Vector3.zero, count) { CutCar = cutCar, CutStarted = Time.time };
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
        GotoMenu.Log?.Error(exception, "Couldn't leave the cars");
        _jobs.Remove(id);
      }
    }
    if (_jobs.Count == 0) Destroy(gameObject);
  }

  private static bool Check(Job job)
  {
    var locomotive = job.Locomotive;
    if (locomotive == null || Time.time - job.Started > TimeoutSeconds) return true;
    if (job.CutCar != null)
    {
      if (!locomotive.EnumerateCoupled().Contains(job.CutCar))
      {
        var away = Delivery.PullAway(locomotive, job.CutCar) ? ", pulling away" : "";
        Toast.Present($"{locomotive.DisplayName} left {Delivery.Cars(job.Count)} at {job.Name}{away}", ToastPosition.Bottom);
        return true;
      }
      if (Time.time - job.CutStarted <= CutTimeoutSeconds) return false;
      Toast.Present($"{locomotive.DisplayName} couldn't uncouple the cars at {job.Name}", ToastPosition.Bottom);
      return true;
    }
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed) job.Moved = true;
    if (Time.time - job.Started <= OrderSettleSeconds) return false;
    var helper = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
    var orders = helper.Orders;
    if (orders.Mode != AutoEngineerMode.Waypoint)
    {
      GotoMenu.Log?.Info($"Delivery to {job.Name} dropped: {locomotive.DisplayName}'s engineer left Waypoint mode");
      return true;
    }
    var current = orders.Waypoint?.LocationString;
    if (current != null && current != job.LocationString)
    {
      GotoMenu.Log?.Info($"Delivery to {job.Name} dropped: {locomotive.DisplayName} got another waypoint");
      return true;
    }
    if (!locomotive.IsStopped(StoppedSeconds)) return false;
    var near = locomotive.EnumerateCoupled().Any(car => Vector3.Distance(car.GetCenterPosition(), job.Target) <= car.carLength / 2f + ArrivalTolerance);
    if (!near && !(job.Moved && current == null)) return false;
    if (current != null) helper.ClearWaypoint();
    if (!Delivery.Cut(locomotive, job.Ids, out var cutCar))
    {
      Toast.Present($"{locomotive.DisplayName} arrived, but the cars for {job.Name} aren't together at one end of the train anymore", ToastPosition.Bottom);
      return true;
    }
    job.CutCar = cutCar;
    job.CutStarted = Time.time;
    return false;
  }

  private sealed class Job
  {
    public Job(BaseLocomotive locomotive, HashSet<string> ids, string name, string locationString, Vector3 target, int count)
    {
      Locomotive = locomotive;
      Ids = ids;
      Name = name;
      LocationString = locationString;
      Target = target;
      Count = count;
      Started = Time.time;
    }

    public BaseLocomotive Locomotive { get; }
    public HashSet<string> Ids { get; }
    public string Name { get; }
    public string LocationString { get; }
    public Vector3 Target { get; }
    public int Count { get; }
    public float Started { get; }
    public bool Moved { get; set; }
    public Car CutCar { get; set; }
    public float CutStarted { get; set; }
  }
}
