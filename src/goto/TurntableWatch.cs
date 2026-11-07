using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Helpers;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal sealed class TurntableWatch : MonoBehaviour
{
  private const float TickSeconds = 0.5f;
  private const float SettleSeconds = 2f;
  private const float MovingSpeed = 0.5f;
  private const float StartSeconds = 10f;
  private const float OrderSettleSeconds = 2f;
  private const float TurnTimeout = 180f;
  private const float MoveTimeout = 900f;
  private const float EntryOffset = 2f;
  private const float ClearMeters = 6f;
  private const float StallMargin = 2f;
  private const float StallSearch = 500f;
  private const float MinCorrection = 0.3f;
  private const int MaxRetries = 2;
  private const float MetersToFeet = 3.28084f;
  private const string TrainOrdersHookKey = "qbanrambo.train-orders.turntable";
  private const float IdleSeconds = 1.5f;
  private const int MaxReissues = 3;
  private const float ResumeDelay = 5f;
  private const float ResumeRetry = 2f;
  private const int ResumeTries = 30;

  private static TurntableWatch _shared;

  private readonly Dictionary<string, Job> _jobs = new();
  private float _nextTick;
  private bool _resumePending;
  private float _resumeAt;
  private int _resumeTries;

  internal static string Refusal(BaseLocomotive locomotive, TurntableSite site)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    if (cars.Any(car => car is not BaseLocomotive && !car.CarType.IsTender)) return "Uncouple the cars first";
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var bridge = site.Bridge.GetLength();
    if (length > bridge) return $"Too long: {length * MetersToFeet:0} ft on a {bridge * MetersToFeet:0} ft bridge";
    return null;
  }

  internal static void Begin(BaseLocomotive locomotive, TurntableSite site, PitTrack goal)
  {
    if (QueuedInTrainOrders(locomotive, site, goal)) return;
    var refusal = Refusal(locomotive, site);
    if (refusal != null)
    {
      Say($"{locomotive.DisplayName}: {refusal.ToLowerInvariant()}");
      return;
    }
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var job = new Job(locomotive, site, goal, cars);
    if (_shared == null) _shared = new GameObject("Goto Turntable Watch").AddComponent<TurntableWatch>();
    _shared._jobs.Remove(locomotive.id);
    if (Start(job)) _shared._jobs[locomotive.id] = job;
    else TurntableMemory.Forget(locomotive.id);
  }

  internal static void ResumeSoon()
  {
    if (_shared == null) _shared = new GameObject("Goto Turntable Watch").AddComponent<TurntableWatch>();
    _shared._resumePending = true;
    _shared._resumeAt = Time.unscaledTime + ResumeDelay;
    _shared._resumeTries = 0;
  }

  private void TryResume()
  {
    if (!_resumePending || Time.unscaledTime < _resumeAt) return;
    var controller = TrainController.Shared;
    if ((controller == null || !controller.Cars.Any() || Graph.Shared == null) && ++_resumeTries < ResumeTries)
    {
      _resumeAt = Time.unscaledTime + ResumeRetry;
      return;
    }
    _resumePending = false;
    if (controller == null) return;
    var records = TurntableMemory.Load();
    if (records.Count == 0) return;
    var sites = Turntables.Sites();
    foreach (var record in records.Values)
    {
      try
      {
        if (_jobs.ContainsKey(record.Locomotive)) continue;
        if (!controller.TryGetCarForId(record.Locomotive, out var car) || car is not BaseLocomotive locomotive) continue;
        var job = Restore(locomotive, sites.FirstOrDefault(site => site.Turntable.id == record.Turntable), record);
        if (job == null)
        {
          TurntableMemory.Forget(record.Locomotive);
          continue;
        }
        _jobs[locomotive.id] = job;
        GotoMenu.Log?.Info($"Resumed the turntable move of {locomotive.DisplayName} at {record.Turntable}");
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Warning($"Couldn't resume the turntable move of {record.Locomotive}: {exception.Message}");
      }
    }
  }

  private static Job Restore(BaseLocomotive locomotive, TurntableSite site, TurntableMemory.Record record)
  {
    if (site == null || Refusal(locomotive, site) != null) return null;
    PitTrack goal = null;
    if (record.Goal >= 0)
    {
      goal = site.Tracks.FirstOrDefault(track => track.Index == record.Goal);
      if (goal == null) return null;
    }
    var job = new Job(locomotive, site, goal, locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList())
    {
      Entry = record.Entry < 0 ? null : site.Tracks.FirstOrDefault(track => track.Index == record.Entry)
    };
    if (record.Phase == (int)Phase.Turning && OnBridge(job) && !site.Turntable.TryGetCarBlockingMovement(out _)) return Line(job, record.Stop, Phase.Turning) ? job : null;
    if (record.Phase != (int)Phase.Leaving) return Start(job) ? job : null;
    if (!Touching(job)) return null;
    job.TargetStop = record.Stop;
    if (IsLined(job)) return Leave(job) ? job : null;
    return Line(job, record.Stop, Phase.Turning) ? job : null;
  }

  private static void Remember(Job job)
  {
    if (job.Locomotive == null) return;
    TurntableMemory.Remember(new TurntableMemory.Record(job.Locomotive.id, job.Site.Turntable.id, job.Goal?.Index ?? -1, (int)job.Phase, job.Entry?.Index ?? -1, job.TargetStop));
  }

  private static bool QueuedInTrainOrders(BaseLocomotive locomotive, TurntableSite site, PitTrack goal)
  {
    try
    {
      return AppDomain.CurrentDomain.GetData(TrainOrdersHookKey) is Func<object, string, int, bool> hook && hook(locomotive, site.Turntable.id, goal?.Index ?? -1);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Train Orders didn't take the turntable move: {exception.Message}");
      return false;
    }
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
    TryResume();
    foreach (var id in _jobs.Keys.ToList())
    {
      var job = _jobs[id];
      try
      {
        if (!Check(job)) continue;
        _jobs.Remove(id);
        if (job.Locomotive != null) TurntableMemory.Forget(id);
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Error(exception, "Couldn't watch the turntable move");
        _jobs.Remove(id);
        TurntableMemory.Forget(id);
        Say("The turntable move failed. See the Message Log.");
      }
    }
    if (_jobs.Count == 0 && !_resumePending) Destroy(gameObject);
  }

  private static bool Start(Job job)
  {
    var site = job.Site;
    if (OnBridge(job)) return site.Turntable.TryGetCarBlockingMovement(out _) ? Enter(job) : Turn(job, announce: true);
    string failure = null;
    var entry = Turntables.TrackUnder(site, job.Cars) ?? NearestLead(job, out failure);
    if (entry == null)
    {
      Say($"{job.Locomotive.DisplayName} can't get to the {site.Name}{(failure == null ? "" : $": {failure}")}");
      return false;
    }
    if (job.Goal != null && job.Goal.Index == entry.Index)
    {
      Say($"{job.Locomotive.DisplayName} is already on {job.Goal.Label}");
      return false;
    }
    job.Entry = entry;
    var released = GotoSettings.ReleaseHandbrakes ? GotoMenu.ReleaseHandbrakes(job.Locomotive) : 0;
    var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
    Say($"{job.Locomotive.DisplayName} heading to the {site.Name}{handbrakes}");
    if (site.Turntable.IsLinedTo(entry.Node)) return Enter(job);
    return Line(job, StopFacing(site, entry.Index, site.Turntable.Angle), Phase.Lining);
  }

  private static PitTrack NearestLead(Job job, out string failure)
  {
    failure = null;
    var length = AutoEngineerPlanner.CalculateTotalLength(job.Cars);
    var forward = Helper(job.Locomotive).Orders.Forward;
    PitTrack best = null;
    var bestDistance = float.MaxValue;
    foreach (var track in job.Site.Tracks.Where(track => !track.Stall))
    {
      var location = job.Site.Turntable.PitLocation(track.Index, EntryOffset);
      if (!location.HasValue) continue;
      var waypoint = new OrderWaypoint(Graph.LocationToString(location.Value), null);
      foreach (var direction in new[] { forward, !forward })
      {
        if (!AutoEngineerPlanner.TryPlanRoute(job.Locomotive, job.Cars, length, direction, waypoint, Graph.Shared, out _, out var route, out var message, out var distance))
        {
          failure ??= message;
          continue;
        }
        if (route != null && route.Any(step => step.Location.segment == job.Site.Bridge)) continue;
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        best = track;
      }
    }
    if (best != null) failure = null;
    return best;
  }

  private static int StopFacing(TurntableSite site, int nodeIndex, float angle)
  {
    var half = site.Stops / 2;
    var first = nodeIndex % site.Stops;
    var second = (nodeIndex + site.Stops - half) % site.Stops;
    var turntable = site.Turntable;
    return Mathf.Abs(Mathf.DeltaAngle(angle, turntable.AngleForIndex(first))) <= Mathf.Abs(Mathf.DeltaAngle(angle, turntable.AngleForIndex(second))) ? first : second;
  }

  private static bool Line(Job job, int stop, Phase phase)
  {
    var site = job.Site;
    job.TargetStop = stop;
    job.Phase = phase;
    job.PhaseStarted = Time.time;
    job.IdleSince = -1f;
    Remember(job);
    if (IsLined(job)) return phase == Phase.Lining ? Enter(job) : Leave(job);
    var controller = site.Controller;
    if (controller != null) controller.TargetAngle = site.Turntable.AngleForIndex(stop);
    return true;
  }

  private static bool IsLined(Job job)
  {
    var controller = job.Site.Controller;
    return controller != null && job.Site.Turntable.StopIndex == job.TargetStop && !controller.TargetAngle.HasValue;
  }

  private static bool Reissue(Job job, out bool failed)
  {
    failed = false;
    var controller = job.Site.Controller;
    if (controller == null || controller.TargetAngle.HasValue)
    {
      job.IdleSince = -1f;
      return controller == null;
    }
    if (job.IdleSince < 0f)
    {
      job.IdleSince = Time.time;
      return true;
    }
    if (Time.time - job.IdleSince < IdleSeconds) return true;
    if (++job.Reissues > MaxReissues)
    {
      failed = true;
      return true;
    }
    GotoMenu.Log?.Info($"Turntable {job.Site.Turntable.id}: idle at stop {job.Site.Turntable.StopIndex?.ToString() ?? "none"}, asking for stop {job.TargetStop} again for {job.Locomotive.DisplayName}");
    job.IdleSince = -1f;
    controller.TargetAngle = job.Site.Turntable.AngleForIndex(job.TargetStop);
    return true;
  }

  private static bool Check(Job job)
  {
    var locomotive = job.Locomotive;
    if (locomotive == null) return true;
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed) job.Moved = true;
    switch (job.Phase)
    {
      case Phase.Lining:
      case Phase.Turning:
        if (job.Phase == Phase.Turning && TookOver(job, out _)) return true;
        if (IsLined(job)) return job.Phase == Phase.Lining ? !Enter(job) : !Leave(job);
        if (Reissue(job, out var failed) && !failed) return false;
        if (!failed && Time.time - job.PhaseStarted < TurnTimeout) return false;
        Say($"The {job.Site.Name} didn't turn{Blocking(job)}");
        return true;
      case Phase.Entering:
        return CheckEntering(job);
      case Phase.Leaving:
        return CheckLeaving(job);
      default:
        return true;
    }
  }

  private static bool Enter(Job job)
  {
    var waypoint = CenterWaypoint(job, out var message);
    if (!waypoint.HasValue)
    {
      Say($"{job.Locomotive.DisplayName} can't get onto the {job.Site.Name}{(message == null ? "" : $": {message}")}");
      return false;
    }
    return Send(job, waypoint.Value, Phase.Entering);
  }

  private static OrderWaypoint? CenterWaypoint(Job job, out string message)
  {
    message = null;
    var graph = Graph.Shared;
    var view = graph.Current;
    var bridge = job.Site.Bridge;
    var length = AutoEngineerPlanner.CalculateTotalLength(job.Cars);
    var center = view.LocationByMoving(new Location(bridge, bridge.GetLength() / 2f, TrackSegment.End.A), job.Shift, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
    PlacementPlan? best = null;
    foreach (var end in new[] { WaypointEnd.A, WaypointEnd.B })
    {
      foreach (var sign in new[] { 1f, -1f })
      {
        Location mark;
        try
        {
          mark = graph.LocationOrientedToward(view.LocationByMoving(center, sign * length / 2f, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true), center);
        }
        catch (Exception)
        {
          continue;
        }
        var plan = PosedWaypoint.Plan(job.Locomotive, view, mark, null, end, WaypointPose.ToBodyToward(mark.end.Flipped()));
        if (!plan.IsPlanned || !plan.Waypoint.HasValue)
        {
          message ??= plan.Message;
          continue;
        }
        if (best.HasValue && plan.RouteDistance >= best.Value.RouteDistance) continue;
        best = plan;
      }
    }
    if (!best.HasValue) return null;
    message = null;
    return best.Value.Waypoint;
  }

  private static bool CheckEntering(Job job)
  {
    if (TookOver(job, out var ours)) return true;
    var elapsed = Time.time - job.PhaseStarted;
    if (!job.Locomotive.IsStopped(SettleSeconds)) return false;
    if (!job.Moved && elapsed < StartSeconds) return false;
    var blocking = job.Site.Turntable.TryGetCarBlockingMovement(out _);
    if (!blocking && OnBridge(job))
    {
      if (ours) Helper(job.Locomotive).ClearWaypoint();
      return !Turn(job, announce: true);
    }
    if (ours && !Touching(job))
    {
      if (elapsed < MoveTimeout) return false;
      Say($"{job.Locomotive.DisplayName} didn't reach the {job.Site.Name}");
      return true;
    }
    var error = CenterError(job);
    if (job.Retries++ >= MaxRetries || Mathf.Abs(error) < MinCorrection)
    {
      if (ours) Helper(job.Locomotive).ClearWaypoint();
      Say($"{job.Locomotive.DisplayName} couldn't stop clear of the {job.Site.Name}'s ends");
      return true;
    }
    GotoMenu.Log?.Info($"Turntable {job.Site.Turntable.id}: {job.Locomotive.DisplayName} stopped {error:0.00} m off the bridge's center, try {job.Retries + 1}");
    job.Shift -= error;
    return !Enter(job);
  }

  private static bool Turn(Job job, bool announce)
  {
    var site = job.Site;
    var current = site.Turntable.StopIndex ?? StopFacing(site, 0, site.Turntable.Angle);
    int stop;
    if (job.Goal == null)
    {
      stop = (current + site.Stops / 2) % site.Stops;
    }
    else
    {
      var graph = Graph.Shared;
      var front = graph.GetPosition(job.Locomotive.LocationFor(Car.End.F));
      var endA = graph.GetPosition(site.Turntable.BridgeLocation(TrackSegment.End.A, 0f));
      var endB = graph.GetPosition(site.Turntable.BridgeLocation(TrackSegment.End.B, 0f));
      var frontIsA = Vector3.Distance(front, endA) <= Vector3.Distance(front, endB);
      stop = frontIsA ? job.Goal.Index % site.Stops : (job.Goal.Index + site.Stops - site.Stops / 2) % site.Stops;
    }
    if (announce) Say($"{job.Locomotive.DisplayName} on the {site.Name}, turning");
    return Line(job, stop, Phase.Turning);
  }

  private static bool Leave(Job job)
  {
    var site = job.Site;
    var exit = job.Goal ?? job.Entry;
    if (exit == null)
    {
      Say($"{job.Locomotive.DisplayName} turned on the {site.Name}");
      return false;
    }
    var start = site.Turntable.PitLocation(exit.Index, 0f);
    if (!start.HasValue)
    {
      Say($"{job.Locomotive.DisplayName} can't find {exit.Label} at the {site.Name}");
      return false;
    }
    var view = Graph.Shared.Current;
    Location target;
    if (exit.Stall)
    {
      var end = view.LocationByMoving(start.Value, StallSearch, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
      target = view.LocationByMoving(end, -StallMargin, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
    }
    else
    {
      var length = AutoEngineerPlanner.CalculateTotalLength(job.Cars);
      target = view.LocationByMoving(start.Value, length + ClearMeters, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
    }
    return Send(job, new OrderWaypoint(Graph.LocationToString(target), null), Phase.Leaving);
  }

  private static bool CheckLeaving(Job job)
  {
    if (TookOver(job, out var ours)) return true;
    var elapsed = Time.time - job.PhaseStarted;
    if (!job.Locomotive.IsStopped(SettleSeconds)) return false;
    if (!job.Moved && elapsed < StartSeconds) return false;
    if (!Touching(job))
    {
      if (ours) Helper(job.Locomotive).ClearWaypoint();
      var exit = job.Goal ?? job.Entry;
      Say(job.Goal == null
        ? $"{job.Locomotive.DisplayName} turned around at the {job.Site.Name}"
        : exit.Stall ? $"{job.Locomotive.DisplayName} parked on {exit.Label} at the {job.Site.Name}" : $"{job.Locomotive.DisplayName} left the {job.Site.Name} on {exit.Label}");
      return true;
    }
    if (elapsed < MoveTimeout) return false;
    var status = new AutoEngineerPersistence(job.Locomotive.KeyValueObject).PlannerStatus;
    Say($"{job.Locomotive.DisplayName} couldn't leave the {job.Site.Name}{(string.IsNullOrEmpty(status) ? "" : $": {status}")}");
    return true;
  }

  private static bool TookOver(Job job, out bool ours)
  {
    var orders = Helper(job.Locomotive).Orders;
    var current = orders.Waypoint?.LocationString;
    ours = current != null && current == job.LocationString;
    if (Time.time - job.PhaseStarted <= OrderSettleSeconds) return false;
    if (orders.Mode == AutoEngineerMode.Waypoint && (current == null || ours)) return false;
    Say($"{job.Locomotive.DisplayName} got other orders, the turntable move stopped");
    return true;
  }

  private static bool Send(Job job, OrderWaypoint waypoint, Phase phase)
  {
    var helper = Helper(job.Locomotive);
    AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
    bool captured;
    try
    {
      helper.SetWaypointOrder(waypoint);
      captured = AppDomain.CurrentDomain.GetData(GotoMenu.CapturedKey) is true;
    }
    finally
    {
      AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
    }
    if (captured)
    {
      Say($"{job.Locomotive.DisplayName}: another mod took the order. Close the Train Orders window and try again");
      return false;
    }
    job.LocationString = waypoint.LocationString;
    job.Phase = phase;
    job.PhaseStarted = Time.time;
    job.Moved = false;
    Remember(job);
    return true;
  }

  private static bool OnBridge(Job job)
  {
    var bridge = job.Site.Bridge;
    return job.Cars.All(car => car.LocationA.segment == bridge || car.LocationB.segment == bridge);
  }

  private static bool Touching(Job job)
  {
    var bridge = job.Site.Bridge;
    return job.Cars.Any(car => car.LocationA.segment == bridge || car.LocationB.segment == bridge) || job.Site.Turntable.TryGetCarBlockingMovement(out _);
  }

  private static float CenterError(Job job)
  {
    var graph = Graph.Shared;
    var site = job.Site;
    var endA = graph.GetPosition(site.Turntable.BridgeLocation(TrackSegment.End.A, 0f));
    var endB = graph.GetPosition(site.Turntable.BridgeLocation(TrackSegment.End.B, 0f));
    var axis = (endB - endA).ZeroY();
    if (axis.sqrMagnitude < 0.01f) return 0f;
    var first = graph.GetPosition(job.Cars[0].LocationA);
    var last = graph.GetPosition(job.Cars[job.Cars.Count - 1].LocationB);
    var middle = (first + last) / 2f;
    var center = (endA + endB) / 2f;
    return Vector3.Dot((middle - center).ZeroY(), axis.normalized);
  }

  private static string Blocking(Job job) => job.Site.Turntable.TryGetCarBlockingMovement(out var car) && car != null ? $": {car.DisplayName} is over its edge" : "";

  private static AutoEngineerOrdersHelper Helper(BaseLocomotive locomotive) => new(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));

  private static void Say(string text) => Toast.Present(text, ToastPosition.Bottom);

  private enum Phase
  {
    Lining,
    Entering,
    Turning,
    Leaving
  }

  private sealed class Job
  {
    public Job(BaseLocomotive locomotive, TurntableSite site, PitTrack goal, List<Car> cars)
    {
      Locomotive = locomotive;
      Site = site;
      Goal = goal;
      Cars = cars;
    }

    public BaseLocomotive Locomotive { get; }
    public TurntableSite Site { get; }
    public PitTrack Goal { get; }
    public List<Car> Cars { get; }
    public PitTrack Entry { get; set; }
    public Phase Phase { get; set; }
    public int TargetStop { get; set; }
    public float PhaseStarted { get; set; }
    public bool Moved { get; set; }
    public int Retries { get; set; }
    public float Shift { get; set; }
    public string LocationString { get; set; }
    public float IdleSince { get; set; } = -1f;
    public int Reissues { get; set; }
  }
}
