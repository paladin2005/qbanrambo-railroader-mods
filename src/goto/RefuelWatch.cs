using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Helpers;
using Model;
using Model.AI;
using Track;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal sealed class RefuelWatch : MonoBehaviour
{
  private const float TickSeconds = 0.5f;
  private const float SettleSeconds = 2f;
  private const float MovingSpeed = 0.5f;
  private const float StartSeconds = 10f;
  private const float OrderSettleSeconds = 2f;
  private const float ArrivalReach = 15f;
  private const float FlowStartSeconds = 15f;
  private const float StallSeconds = 20f;
  private const float BackOffDistance = 10f;
  private const float BackOffBelow = 2f;
  private const float DefaultLead = 0.6f;
  private const float MaxLead = 3f;
  private const float TravelSeen = 1f;
  private const int MaxRetries = 2;
  private const float TimeoutSeconds = 3600f;
  private const float Progress = 0.0001f;
  private const float ResumeDelay = 5f;
  private const float ResumeRetry = 2f;
  private const int ResumeTries = 30;

  private static RefuelWatch _shared;

  private readonly Dictionary<string, Job> _jobs = new();
  private float _nextTick;
  private bool _resumePending;
  private float _resumeAt;
  private int _resumeTries;

  internal static void Begin(BaseLocomotive locomotive, List<FuelStop> stops)
  {
    var fuelCar = Fuel.FuelCar(locomotive);
    var pending = stops.Where(stop => Fuel.Slot(fuelCar, stop.Load) is { } slot && !Fuel.IsFull(slot, stop.Load)).ToList();
    if (pending.Count == 0)
    {
      Say($"{locomotive.DisplayName} is already full");
      return;
    }
    if (_shared == null) _shared = new GameObject("Goto Refuel Watch").AddComponent<RefuelWatch>();
    if (_shared._jobs.TryGetValue(locomotive.id, out var previous)) Stop(previous);
    var ordered = pending.Count == 1 ? pending : pending.OrderBy(stop => Distance(locomotive, fuelCar, stop)).ToList();
    var job = new Job(locomotive, ordered);
    _shared._jobs[locomotive.id] = job;
    if (Advance(job, announce: true)) return;
    _shared._jobs.Remove(locomotive.id);
    RefuelMemory.Forget(locomotive.id);
  }

  internal static void ResumeSoon()
  {
    if (_shared == null) _shared = new GameObject("Goto Refuel Watch").AddComponent<RefuelWatch>();
    _shared._resumePending = true;
    _shared._resumeAt = Time.unscaledTime + ResumeDelay;
    _shared._resumeTries = 0;
  }

  internal static void Drop()
  {
    if (_shared != null)
    {
      _shared._jobs.Clear();
      Destroy(_shared.gameObject);
    }
    _shared = null;
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
    var records = RefuelMemory.Load();
    if (records.Count == 0) return;
    List<FuelStop> all = null;
    foreach (var record in records.Values)
    {
      try
      {
        if (_jobs.ContainsKey(record.Locomotive)) continue;
        if (!controller.TryGetCarForId(record.Locomotive, out var car) || car is not BaseLocomotive locomotive) continue;
        all ??= Fuel.Stops();
        var stops = record.Stops.Select(id => all.FirstOrDefault(stop => stop.Id == id)).Where(stop => stop != null).ToList();
        var job = stops.Count > 0 ? Restore(locomotive, stops, record) : null;
        if (job == null)
        {
          RefuelMemory.Forget(record.Locomotive);
          continue;
        }
        _jobs[locomotive.id] = job;
        GotoMenu.Log?.Info($"Resumed refuelling {locomotive.DisplayName} at {job.Stop.Id}");
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Warning($"Couldn't resume the refuelling of {record.Locomotive}: {exception.Message}");
      }
    }
  }

  private static Job Restore(BaseLocomotive locomotive, List<FuelStop> stops, RefuelMemory.Record record)
  {
    var job = new Job(locomotive, stops);
    var slot = SlotFor(job);
    if (slot == null || Fuel.IsFull(slot, job.Stop.Load)) return null;
    var orders = Helper(locomotive).Orders;
    if (record.Phase != nameof(Phase.Loading) && orders.Mode == AutoEngineerMode.Waypoint && orders.Waypoint?.LocationString == record.Location)
    {
      job.Phase = record.Phase == nameof(Phase.BackingOff) ? Phase.BackingOff : Phase.Moving;
      job.LocationString = record.Location;
      job.PhaseStarted = Time.time;
      job.StartPoint = Fuel.TargetPoint(slot);
      return job;
    }
    if (!locomotive.IsStopped(SettleSeconds) || Fuel.Offset(slot, job.Stop) > ArrivalReach) return null;
    if (Fuel.LinedUp(slot, job.Stop)) return StartLoading(job, slot) ? job : null;
    return Advance(job, announce: false) ? job : null;
  }

  private static void Remember(Job job)
  {
    if (job.Locomotive == null) return;
    RefuelMemory.Remember(job.Locomotive.id, job.Phase.ToString(), job.LocationString, new[] { job.Stop }.Concat(job.Stops).Select(stop => stop.Id));
  }

  internal static void Clear()
  {
    if (_shared != null)
    {
      foreach (var job in _shared._jobs.Values) Stop(job);
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
        if (job.Locomotive != null) RefuelMemory.Forget(id);
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Error(exception, "Couldn't watch the refuelling");
        Stop(job);
        _jobs.Remove(id);
        RefuelMemory.Forget(id);
        Say("Refuelling failed. See the Message Log.");
      }
    }
    if (_jobs.Count == 0 && !_resumePending) Destroy(gameObject);
  }

  private static float Distance(BaseLocomotive locomotive, Car fuelCar, FuelStop stop)
  {
    var slot = Fuel.Slot(fuelCar, stop.Load);
    if (slot == null) return float.MaxValue;
    if (Fuel.LinedUp(slot, stop)) return -1f;
    return Fuel.Plan(locomotive, slot, stop, DefaultLead, out _) is { } plan ? plan.RouteDistance : float.MaxValue;
  }

  private static bool Check(Job job)
  {
    var locomotive = job.Locomotive;
    if (locomotive == null) return true;
    if (Time.time - job.Started > TimeoutSeconds)
    {
      Stop(job);
      return true;
    }
    var slot = SlotFor(job);
    if (slot == null)
    {
      Stop(job);
      Say($"{locomotive.DisplayName} has no tender coupled, refuelling stopped");
      return true;
    }
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed) job.Moved = true;
    return job.Phase == Phase.Loading ? CheckLoading(job, slot) : CheckMoving(job, slot);
  }

  private static bool CheckMoving(Job job, FuelSlot slot)
  {
    var locomotive = job.Locomotive;
    var stop = job.Stop;
    var helper = Helper(locomotive);
    var orders = helper.Orders;
    var elapsed = Time.time - job.PhaseStarted;
    var current = orders.Waypoint?.LocationString;
    var ours = current == job.LocationString;
    var settled = elapsed > OrderSettleSeconds;
    if (settled && (orders.Mode != AutoEngineerMode.Waypoint || (current != null && !ours))) return true;
    if (!locomotive.IsStopped(SettleSeconds)) return false;
    if (!job.Moved && elapsed < StartSeconds) return false;
    if (job.Phase == Phase.BackingOff)
    {
      if (ours) helper.ClearWaypoint();
      return !Advance(job, announce: false);
    }
    var offset = Fuel.Offset(slot, stop);
    if (offset > ArrivalReach)
    {
      if (ours || !settled) return false;
      Say($"{locomotive.DisplayName} stopped short of {Place(stop)}");
      return true;
    }
    if (ours) helper.ClearWaypoint();
    var tolerance = Fuel.Tolerance(slot, stop);
    var along = Fuel.Along(slot, stop);
    var moved = Vector3.Dot((Fuel.TargetPoint(slot) - job.StartPoint).ZeroY(), Fuel.TrackDirection(stop));
    var travel = Mathf.Abs(moved) >= TravelSeen ? Mathf.Sign(moved) : job.Travel;
    GotoMenu.Log?.Info($"Refuel {locomotive.DisplayName} at {stop.Id} ({stop.LoadName}): {along:0.00} m off the spout, tolerance {tolerance:0.00} m, lead {job.Lead:0.00} m, try {job.Retries + 1}");
    if (Mathf.Abs(along) <= tolerance) return !StartLoading(job, slot);
    if (++job.Retries > MaxRetries)
    {
      Say($"{locomotive.DisplayName} couldn't line up with {Place(stop)}");
      return !NextStop(job);
    }
    if (travel != 0f) job.Lead = Mathf.Clamp(job.Lead - along * travel, -MaxLead, MaxLead);
    if (Mathf.Abs(along) < BackOffBelow && Fuel.BackOff(locomotive, stop, BackOffDistance, travel) is { } away) return !Send(job, away, Phase.BackingOff, announce: false);
    return !Advance(job, announce: false);
  }

  private static bool CheckLoading(Job job, FuelSlot slot)
  {
    var locomotive = job.Locomotive;
    var stop = job.Stop;
    if (Mathf.Abs(locomotive.velocity) > MovingSpeed)
    {
      Release(job);
      Say($"{locomotive.DisplayName} moved, refuelling stopped");
      return true;
    }
    var level = Fuel.Level(slot, stop.Load);
    if (Fuel.IsFull(slot, stop.Load))
    {
      Release(job);
      Say($"{locomotive.DisplayName}: {stop.LoadName.ToLowerInvariant()} full");
      return !NextStop(job);
    }
    if (level > job.LastLevel + Progress)
    {
      job.LastLevel = level;
      job.LastProgress = Time.time;
      job.Flowing = true;
    }
    if (Time.time - job.LastProgress < (job.Flowing ? StallSeconds : FlowStartSeconds)) return false;
    Release(job);
    GotoMenu.Log?.Info($"Refuel {locomotive.DisplayName} at {stop.Id} stalled at {level * 100f:0}%, {Fuel.Along(slot, stop):0.00} m off the spout");
    Say(Fuel.InStock(stop)
      ? $"{locomotive.DisplayName} isn't taking {stop.LoadName.ToLowerInvariant()} at {Place(stop)}"
      : $"{Place(stop)} ran out of {stop.LoadName.ToLowerInvariant()}");
    return !NextStop(job);
  }

  private static bool NextStop(Job job)
  {
    if (job.Stops.Count == 0) return false;
    job.Stop = job.Stops.Dequeue();
    job.Retries = 0;
    return Advance(job, announce: false);
  }

  private static bool Advance(Job job, bool announce)
  {
    while (true)
    {
      if (Approach(job, announce)) return true;
      if (job.Captured || job.Stops.Count == 0) return false;
      job.Stop = job.Stops.Dequeue();
      job.Retries = 0;
      announce = false;
    }
  }

  private static bool Approach(Job job, bool announce)
  {
    var locomotive = job.Locomotive;
    var stop = job.Stop;
    var slot = SlotFor(job);
    if (slot == null || Fuel.IsFull(slot, stop.Load)) return false;
    if (!Fuel.InStock(stop))
    {
      Say($"{Place(stop)} has no {stop.LoadName.ToLowerInvariant()} in stock");
      return false;
    }
    if (Fuel.LinedUp(slot, stop))
    {
      if (announce) Say($"{locomotive.DisplayName} refuelling at {Place(stop)}");
      return StartLoading(job, slot);
    }
    var plan = Fuel.Plan(locomotive, slot, stop, job.Lead, out var message);
    if (plan.HasValue)
    {
      job.Travel = plan.Value.Travel;
      return Send(job, plan.Value.Waypoint, Phase.Moving, announce);
    }
    if (message != null && message.Contains(Fuel.AlreadyPositioned))
    {
      var away = Fuel.BackOff(locomotive, stop, BackOffDistance, job.Travel);
      if (away.HasValue) return Send(job, away.Value, Phase.BackingOff, announce);
    }
    Say(message ?? $"{locomotive.DisplayName} can't get to {Place(stop)}");
    return false;
  }

  private static bool Send(Job job, OrderWaypoint waypoint, Phase phase, bool announce)
  {
    var locomotive = job.Locomotive;
    var helper = Helper(locomotive);
    var key = string.Join(",", new[] { job.Stop }.Concat(job.Stops).Select(stop => stop.Id));
    AppDomain.CurrentDomain.SetData(Fuel.RefuelKey, announce ? key : null);
    AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
    var queued = false;
    try
    {
      helper.SetWaypointOrder(waypoint);
      queued = announce && AppDomain.CurrentDomain.GetData(Fuel.RefuelKey) == null;
      job.Captured = queued || AppDomain.CurrentDomain.GetData(GotoMenu.CapturedKey) is true;
    }
    finally
    {
      AppDomain.CurrentDomain.SetData(Fuel.RefuelKey, null);
      AppDomain.CurrentDomain.SetData(GotoMenu.CapturedKey, null);
    }
    if (job.Captured)
    {
      if (!queued) Say($"{locomotive.DisplayName}: another mod took the order as a plain move. Update Train Orders, or close its window and refuel again");
      return false;
    }
    job.LocationString = waypoint.LocationString;
    job.Phase = phase;
    job.Moved = false;
    job.PhaseStarted = Time.time;
    if (SlotFor(job) is { } slot) job.StartPoint = Fuel.TargetPoint(slot);
    Remember(job);
    if (!announce) return true;
    var released = GotoSettings.ReleaseHandbrakes ? GotoMenu.ReleaseHandbrakes(locomotive) : 0;
    var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
    Say($"{locomotive.DisplayName} heading to {Place(job.Stop)} for {Loads(job)}{handbrakes}");
    return true;
  }

  private static bool StartLoading(Job job, FuelSlot slot)
  {
    Fuel.Request(job.Stop, true);
    Fuel.Hatch(slot, job.Stop.Load, true);
    job.Phase = Phase.Loading;
    job.PhaseStarted = Time.time;
    job.LastLevel = Fuel.Level(slot, job.Stop.Load);
    job.LastProgress = Time.time;
    job.Flowing = false;
    Remember(job);
    return true;
  }

  private static void Stop(Job job)
  {
    if (job.Phase != Phase.Loading) return;
    try
    {
      Release(job);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Couldn't stop the loader {job.Stop.Id}: {exception.Message}");
    }
  }

  private static void Release(Job job)
  {
    Fuel.Request(job.Stop, false);
    Fuel.Hatch(SlotFor(job), job.Stop.Load, false);
  }

  private static FuelSlot SlotFor(Job job) => Fuel.Slot(Fuel.FuelCar(job.Locomotive), job.Stop.Load);

  private static AutoEngineerOrdersHelper Helper(BaseLocomotive locomotive) => new(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));

  private static string Place(FuelStop stop) => string.IsNullOrEmpty(stop.Town) ? stop.Name : $"{stop.Name} at {stop.Town}";

  private static string Loads(Job job)
  {
    var names = new[] { job.Stop }.Concat(job.Stops).Select(stop => stop.LoadName.ToLowerInvariant()).Distinct().ToList();
    return names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[names.Count - 1]}";
  }

  private static void Say(string text) => Toast.Present(text, ToastPosition.Bottom);

  private enum Phase
  {
    Moving,
    BackingOff,
    Loading
  }

  private sealed class Job
  {
    public Job(BaseLocomotive locomotive, List<FuelStop> stops)
    {
      Locomotive = locomotive;
      Stop = stops[0];
      Stops = new Queue<FuelStop>(stops.Skip(1));
      Started = Time.time;
    }

    public BaseLocomotive Locomotive { get; }
    public Queue<FuelStop> Stops { get; }
    public FuelStop Stop { get; set; }
    public float Started { get; }
    public Phase Phase { get; set; }
    public string LocationString { get; set; }
    public float PhaseStarted { get; set; }
    public bool Moved { get; set; }
    public int Retries { get; set; }
    public float LastLevel { get; set; }
    public float LastProgress { get; set; }
    public bool Flowing { get; set; }
    public bool Captured { get; set; }
    public float Lead { get; set; } = DefaultLead;
    public float Travel { get; set; }
    public Vector3 StartPoint { get; set; }
  }
}
