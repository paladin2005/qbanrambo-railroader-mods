using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Helpers;
using Model;
using Model.AI;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal sealed class FuelRun
{
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

  private readonly BaseLocomotive _locomotive;
  private readonly Queue<FuelStop> _stops;
  private readonly float _started;
  private FuelStop _stop;
  private Phase _phase;
  private string _locationString;
  private float _phaseStarted;
  private bool _moved;
  private int _retries;
  private float _lastLevel;
  private float _lastProgress;
  private bool _flowing;
  private float _lead = DefaultLead;
  private float _travel;
  private Vector3 _startPoint;

  internal FuelRun(BaseLocomotive locomotive, List<FuelStop> stops)
  {
    _locomotive = locomotive;
    var fuelCar = Fuel.FuelCar(locomotive);
    var ordered = stops.Count == 1 ? stops : stops.OrderBy(stop => Distance(fuelCar, stop)).ToList();
    _stop = ordered[0];
    _stops = new Queue<FuelStop>(ordered.Skip(1));
    _started = Time.time;
  }

  internal bool Done { get; private set; }

  internal string Problem { get; private set; }

  internal void Begin() => Advance();

  internal string Detail => _phase == Phase.Loading && SlotFor() is { } slot ? $"{_stop.LoadName.ToLowerInvariant()} {Fuel.Level(slot, _stop.Load) * 100f:0}%" : null;

  internal void Tick()
  {
    if (Done || Problem != null) return;
    if (Time.time - _started > TimeoutSeconds)
    {
      Fail("Refuelling took too long");
      return;
    }
    var slot = SlotFor();
    if (slot == null)
    {
      Fail("No tender coupled");
      return;
    }
    if (Mathf.Abs(_locomotive.velocity) > MovingSpeed) _moved = true;
    if (_phase == Phase.Loading) CheckLoading(slot);
    else CheckMoving(slot);
  }

  internal void Release()
  {
    if (_phase != Phase.Loading) return;
    try
    {
      Fuel.Request(_stop, false);
      Fuel.Hatch(SlotFor(), _stop.Load, false);
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Warning($"Couldn't stop the loader {_stop.Id}: {exception.Message}");
    }
    _phase = Phase.Moving;
  }

  private float Distance(Car fuelCar, FuelStop stop)
  {
    var slot = Fuel.Slot(fuelCar, stop.Load);
    if (slot == null) return float.MaxValue;
    if (Fuel.LinedUp(slot, stop)) return -1f;
    return Fuel.Plan(_locomotive, slot, stop, DefaultLead, out _) is { } plan ? plan.RouteDistance : float.MaxValue;
  }

  private void CheckMoving(FuelSlot slot)
  {
    var helper = Helper();
    var orders = helper.Orders;
    var elapsed = Time.time - _phaseStarted;
    var current = orders.Waypoint?.LocationString;
    var ours = current == _locationString;
    var settled = elapsed > OrderSettleSeconds;
    if (settled && (orders.Mode != AutoEngineerMode.Waypoint || (current != null && !ours)))
    {
      Fail("The engineer's orders were changed");
      return;
    }
    if (!_locomotive.IsStopped(SettleSeconds)) return;
    if (!_moved && elapsed < StartSeconds) return;
    if (_phase == Phase.BackingOff)
    {
      if (ours) helper.ClearWaypoint();
      Advance();
      return;
    }
    if (Fuel.Offset(slot, _stop) > ArrivalReach)
    {
      if (ours || !settled) return;
      Fail($"Stopped short of {Place(_stop)}");
      return;
    }
    if (ours) helper.ClearWaypoint();
    var tolerance = Fuel.Tolerance(slot, _stop);
    var along = Fuel.Along(slot, _stop);
    var moved = Vector3.Dot((Fuel.TargetPoint(slot) - _startPoint).ZeroY(), Fuel.TrackDirection(_stop));
    var travel = Mathf.Abs(moved) >= TravelSeen ? Mathf.Sign(moved) : _travel;
    TrainOrdersMod.Log?.Info($"Refuel {_locomotive.DisplayName} at {_stop.Id} ({_stop.LoadName}): {along:0.00} m off the spout, tolerance {tolerance:0.00} m, lead {_lead:0.00} m, try {_retries + 1}");
    if (Mathf.Abs(along) <= tolerance)
    {
      StartLoading(slot);
      return;
    }
    if (++_retries > MaxRetries)
    {
      Fail($"Couldn't line up with {Place(_stop)}");
      return;
    }
    if (travel != 0f) _lead = Mathf.Clamp(_lead - along * travel, -MaxLead, MaxLead);
    if (Mathf.Abs(along) < BackOffBelow && Fuel.BackOff(_locomotive, _stop, BackOffDistance, travel) is { } away)
    {
      Send(away, Phase.BackingOff);
      return;
    }
    Advance();
  }

  private void CheckLoading(FuelSlot slot)
  {
    if (Mathf.Abs(_locomotive.velocity) > MovingSpeed)
    {
      Release();
      Fail("The train moved while refuelling");
      return;
    }
    var level = Fuel.Level(slot, _stop.Load);
    if (Fuel.IsFull(slot, _stop.Load))
    {
      Release();
      NextStop();
      return;
    }
    if (level > _lastLevel + Progress)
    {
      _lastLevel = level;
      _lastProgress = Time.time;
      _flowing = true;
    }
    if (Time.time - _lastProgress < (_flowing ? StallSeconds : FlowStartSeconds)) return;
    Release();
    if (Fuel.InStock(_stop))
    {
      Fail($"Not taking {_stop.LoadName.ToLowerInvariant()} at {Place(_stop)}");
      return;
    }
    Say($"{_locomotive.DisplayName}: {Place(_stop)} ran out of {_stop.LoadName.ToLowerInvariant()}");
    NextStop();
  }

  private void NextStop()
  {
    if (_stops.Count == 0)
    {
      Done = true;
      return;
    }
    _stop = _stops.Dequeue();
    _retries = 0;
    Advance();
  }

  private void Advance()
  {
    while (Problem == null)
    {
      if (Approach()) return;
      if (Problem != null) return;
      if (_stops.Count == 0)
      {
        Done = true;
        return;
      }
      _stop = _stops.Dequeue();
      _retries = 0;
    }
  }

  private bool Approach()
  {
    var slot = SlotFor();
    if (slot == null || Fuel.IsFull(slot, _stop.Load)) return false;
    if (!Fuel.InStock(_stop))
    {
      Say($"{_locomotive.DisplayName}: {Place(_stop)} has no {_stop.LoadName.ToLowerInvariant()} in stock");
      return false;
    }
    if (Fuel.LinedUp(slot, _stop))
    {
      StartLoading(slot);
      return true;
    }
    var plan = Fuel.Plan(_locomotive, slot, _stop, _lead, out var message);
    if (plan.HasValue)
    {
      _travel = plan.Value.Travel;
      Send(plan.Value.Waypoint, Phase.Moving);
      return true;
    }
    if (message != null && message.Contains(Fuel.AlreadyPositioned) && Fuel.BackOff(_locomotive, _stop, BackOffDistance, _travel) is { } away)
    {
      Send(away, Phase.BackingOff);
      return true;
    }
    Fail(message ?? $"Can't get to {Place(_stop)}");
    return false;
  }

  private void Send(OrderWaypoint waypoint, Phase phase)
  {
    Maneuvers.ReleaseHandbrakes(_locomotive);
    OrderCapture.Dispatching = true;
    try
    {
      Helper().SetWaypointOrder(waypoint);
    }
    finally
    {
      OrderCapture.Dispatching = false;
    }
    _locationString = waypoint.LocationString;
    _phase = phase;
    _moved = false;
    _phaseStarted = Time.time;
    if (SlotFor() is { } slot) _startPoint = Fuel.TargetPoint(slot);
  }

  private void StartLoading(FuelSlot slot)
  {
    Fuel.Request(_stop, true);
    Fuel.Hatch(slot, _stop.Load, true);
    _phase = Phase.Loading;
    _phaseStarted = Time.time;
    _lastLevel = Fuel.Level(slot, _stop.Load);
    _lastProgress = Time.time;
    _flowing = false;
  }

  private void Fail(string problem) => Problem ??= problem;

  private FuelSlot SlotFor() => Fuel.Slot(Fuel.FuelCar(_locomotive), _stop.Load);

  private AutoEngineerOrdersHelper Helper() => new(_locomotive, new AutoEngineerPersistence(_locomotive.KeyValueObject));

  private static string Place(FuelStop stop) => string.IsNullOrEmpty(stop.Town) ? stop.Name : $"{stop.Name} at {stop.Town}";

  private static void Say(string text) => Toast.Present(text, ToastPosition.Bottom);

  private enum Phase
  {
    Moving,
    BackingOff,
    Loading
  }
}
