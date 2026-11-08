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
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal sealed class TurntableRun
{
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
  private const float IdleSeconds = 1.5f;
  private const int MaxReissues = 3;

  private readonly BaseLocomotive _locomotive;
  private readonly TurntableSite _site;
  private readonly PitTrack _goal;
  private readonly List<Car> _cars;
  private readonly string _saved;
  private readonly Action<string> _save;
  private PitTrack _entry;
  private Phase _phase;
  private int _targetStop;
  private float _phaseStarted;
  private bool _moved;
  private int _retries;
  private float _shift;
  private string _locationString;
  private float _idleSince = -1f;
  private int _reissues;

  internal TurntableRun(BaseLocomotive locomotive, TurntableSite site, PitTrack goal, string saved, Action<string> save)
  {
    _locomotive = locomotive;
    _site = site;
    _goal = goal;
    _saved = saved;
    _save = save;
    _cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
  }

  private TurntableController Controller => _site.Controller;

  internal bool Done { get; private set; }

  internal string Problem { get; private set; }

  internal string Detail => Done || Problem != null ? null : _phase switch
  {
    Phase.Lining => "lining up",
    Phase.Entering => "getting on",
    Phase.Turning => "turning",
    Phase.Leaving => "getting off",
    _ => null
  };

  internal static string Refusal(BaseLocomotive locomotive, TurntableSite site)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    if (cars.Any(car => car is not BaseLocomotive && !car.CarType.IsTender)) return "Uncouple the cars before the turntable";
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var bridge = site.Bridge.GetLength();
    return length > bridge ? $"Too long for the turntable: {length * MetersToFeet:0} ft on a {bridge * MetersToFeet:0} ft bridge" : null;
  }

  internal void Begin()
  {
    Maneuvers.ReleaseHandbrakes(_locomotive);
    if (Resume()) return;
    if (OnBridge())
    {
      if (_site.Turntable.TryGetCarBlockingMovement(out _)) Enter();
      else Turn();
      return;
    }
    string failure = null;
    var entry = Turntables.TrackUnder(_site, _cars) ?? NearestLead(out failure);
    if (entry == null)
    {
      Fail($"Can't get to the {_site.Name}{(failure == null ? "" : $": {failure}")}");
      return;
    }
    if (_goal != null && _goal.Index == entry.Index)
    {
      Done = true;
      return;
    }
    _entry = entry;
    if (_site.Turntable.IsLinedTo(entry.Node)) Enter();
    else Line(StopFacing(entry.Index, _site.Turntable.Angle), Phase.Lining);
  }

  private bool Resume()
  {
    var parts = (_saved ?? "").Split(',');
    if (parts.Length != 3 || !int.TryParse(parts[0], out var phase) || !int.TryParse(parts[1], out var entry) || !int.TryParse(parts[2], out var stop)) return false;
    _entry = entry < 0 ? null : _site.Tracks.FirstOrDefault(track => track.Index == entry);
    if (phase == (int)Phase.Turning && OnBridge())
    {
      TrainOrdersMod.Log?.Info($"Turntable {_site.Turntable.id}: resuming the turn of {_locomotive.DisplayName}");
      Line(stop, Phase.Turning);
      return true;
    }
    if (phase != (int)Phase.Leaving) return false;
    if (!Touching())
    {
      Done = true;
      return true;
    }
    TrainOrdersMod.Log?.Info($"Turntable {_site.Turntable.id}: resuming {_locomotive.DisplayName} getting off");
    _targetStop = stop;
    if (IsLined()) Leave();
    else Line(stop, Phase.Turning);
    return true;
  }

  private void Remember() => _save?.Invoke($"{(int)_phase},{_entry?.Index ?? -1},{_targetStop}");

  internal void Tick()
  {
    if (Done || Problem != null || _locomotive == null) return;
    if (Mathf.Abs(_locomotive.velocity) > MovingSpeed) _moved = true;
    switch (_phase)
    {
      case Phase.Lining:
      case Phase.Turning:
        if (_phase == Phase.Turning && TookOver(out _)) return;
        if (IsLined())
        {
          if (_phase == Phase.Lining) Enter();
          else Leave();
          return;
        }
        if (Reissue()) return;
        if (Time.time - _phaseStarted >= TurnTimeout) Fail($"The {_site.Name} didn't turn{Blocking()}");
        return;
      case Phase.Entering:
        CheckEntering();
        return;
      case Phase.Leaving:
        CheckLeaving();
        return;
    }
  }

  private PitTrack NearestLead(out string failure)
  {
    failure = null;
    var length = AutoEngineerPlanner.CalculateTotalLength(_cars);
    var forward = Helper().Orders.Forward;
    PitTrack best = null;
    var bestDistance = float.MaxValue;
    foreach (var track in _site.Tracks.Where(track => !track.Stall))
    {
      var location = _site.Turntable.PitLocation(track.Index, EntryOffset);
      if (!location.HasValue) continue;
      var waypoint = new OrderWaypoint(Graph.LocationToString(location.Value), null);
      foreach (var direction in new[] { forward, !forward })
      {
        if (!AutoEngineerPlanner.TryPlanRoute(_locomotive, _cars, length, direction, waypoint, Graph.Shared, out _, out var route, out var message, out var distance))
        {
          failure ??= message;
          continue;
        }
        if (route != null && route.Any(step => step.Location.segment == _site.Bridge)) continue;
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        best = track;
      }
    }
    if (best != null) failure = null;
    return best;
  }

  private int StopFacing(int nodeIndex, float angle)
  {
    var stops = _site.Stops;
    var first = nodeIndex % stops;
    var second = (nodeIndex + stops - stops / 2) % stops;
    var turntable = _site.Turntable;
    return Mathf.Abs(Mathf.DeltaAngle(angle, turntable.AngleForIndex(first))) <= Mathf.Abs(Mathf.DeltaAngle(angle, turntable.AngleForIndex(second))) ? first : second;
  }

  private void Line(int stop, Phase phase)
  {
    _targetStop = stop;
    _phase = phase;
    _phaseStarted = Time.time;
    _idleSince = -1f;
    if (phase == Phase.Turning) Remember();
    if (IsLined())
    {
      if (phase == Phase.Lining) Enter();
      else Leave();
      return;
    }
    var controller = Controller;
    if (controller != null) controller.TargetAngle = _site.Turntable.AngleForIndex(stop);
  }

  private bool IsLined()
  {
    var controller = Controller;
    return controller != null && _site.Turntable.StopIndex == _targetStop && !controller.TargetAngle.HasValue;
  }

  private bool Reissue()
  {
    var controller = Controller;
    if (controller == null || controller.TargetAngle.HasValue)
    {
      _idleSince = -1f;
      return controller == null;
    }
    if (_idleSince < 0f)
    {
      _idleSince = Time.time;
      return true;
    }
    if (Time.time - _idleSince < IdleSeconds) return true;
    if (++_reissues > MaxReissues)
    {
      Fail($"The {_site.Name} didn't turn{Blocking()}");
      return true;
    }
    if (_phase == Phase.Turning && _goal != null) _targetStop = GoalStop();
    TrainOrdersMod.Log?.Info($"Turntable {_site.Turntable.id}: idle at stop {_site.Turntable.StopIndex?.ToString() ?? "none"}, asking for stop {_targetStop} again for {_locomotive.DisplayName}");
    _idleSince = -1f;
    controller.TargetAngle = _site.Turntable.AngleForIndex(_targetStop);
    return true;
  }

  private void Enter()
  {
    var waypoint = CenterWaypoint(out var message);
    if (!waypoint.HasValue)
    {
      Fail($"Can't get onto the {_site.Name}{(message == null ? "" : $": {message}")}");
      return;
    }
    Send(waypoint.Value, Phase.Entering);
  }

  private OrderWaypoint? CenterWaypoint(out string message)
  {
    message = null;
    var graph = Graph.Shared;
    var view = graph.Current;
    var bridge = _site.Bridge;
    var length = AutoEngineerPlanner.CalculateTotalLength(_cars);
    var center = view.LocationByMoving(new Location(bridge, bridge.GetLength() / 2f, TrackSegment.End.A), _shift, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
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
        var plan = PosedWaypoint.Plan(_locomotive, view, mark, null, end, WaypointPose.ToBodyToward(mark.end.Flipped()));
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

  private void CheckEntering()
  {
    if (TookOver(out var ours)) return;
    var elapsed = Time.time - _phaseStarted;
    if (!_locomotive.IsStopped(SettleSeconds)) return;
    if (!_moved && elapsed < StartSeconds) return;
    var blocking = _site.Turntable.TryGetCarBlockingMovement(out _);
    if (!blocking && OnBridge())
    {
      if (ours) Helper().ClearWaypoint();
      Turn();
      return;
    }
    if (ours && !Touching())
    {
      if (elapsed >= MoveTimeout) Fail($"Didn't reach the {_site.Name}");
      return;
    }
    var error = CenterError();
    if (_retries++ >= MaxRetries || Mathf.Abs(error) < MinCorrection)
    {
      if (ours) Helper().ClearWaypoint();
      Fail($"Couldn't stop clear of the {_site.Name}'s ends");
      return;
    }
    TrainOrdersMod.Log?.Info($"Turntable {_site.Turntable.id}: {_locomotive.DisplayName} stopped {error:0.00} m off the bridge's center, try {_retries + 1}");
    _shift -= error;
    Enter();
  }

  private void Turn()
  {
    var stops = _site.Stops;
    var current = _site.Turntable.StopIndex ?? StopFacing(0, _site.Turntable.Angle);
    Line(_goal == null ? (current + stops / 2) % stops : GoalStop(), Phase.Turning);
  }

  private int GoalStop()
  {
    var stops = _site.Stops;
    var graph = Graph.Shared;
    var front = graph.GetPosition(_locomotive.LocationFor(Car.End.F));
    var endA = graph.GetPosition(_site.Turntable.BridgeLocation(TrackSegment.End.A, 0f));
    var endB = graph.GetPosition(_site.Turntable.BridgeLocation(TrackSegment.End.B, 0f));
    var frontIsA = Vector3.Distance(front, endA) <= Vector3.Distance(front, endB);
    return frontIsA ? _goal.Index % stops : (_goal.Index + stops - stops / 2) % stops;
  }

  private void Leave()
  {
    var exit = _goal ?? _entry;
    if (exit == null)
    {
      Done = true;
      return;
    }
    var start = _site.Turntable.PitLocation(exit.Index, 0f);
    if (!start.HasValue)
    {
      Fail($"{exit.Label} is gone from the {_site.Name}");
      return;
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
      target = view.LocationByMoving(start.Value, AutoEngineerPlanner.CalculateTotalLength(_cars) + ClearMeters, checkSwitchAgainstMovement: false, stopAtEndOfTrack: true);
    }
    Send(new OrderWaypoint(Graph.LocationToString(target), null), Phase.Leaving);
  }

  private void CheckLeaving()
  {
    if (TookOver(out var ours)) return;
    var elapsed = Time.time - _phaseStarted;
    if (!_locomotive.IsStopped(SettleSeconds)) return;
    if (!_moved && elapsed < StartSeconds) return;
    if (!Touching())
    {
      if (ours) Helper().ClearWaypoint();
      Done = true;
      return;
    }
    if (elapsed < MoveTimeout) return;
    var status = new AutoEngineerPersistence(_locomotive.KeyValueObject).PlannerStatus;
    Fail($"Couldn't leave the {_site.Name}{(string.IsNullOrEmpty(status) ? "" : $": {status}")}");
  }

  private bool TookOver(out bool ours)
  {
    var orders = Helper().Orders;
    var current = orders.Waypoint?.LocationString;
    ours = current != null && current == _locationString;
    if (Time.time - _phaseStarted <= OrderSettleSeconds) return false;
    if (orders.Mode == AutoEngineerMode.Waypoint && (current == null || ours)) return false;
    Fail("The engineer's orders were changed");
    return true;
  }

  private void Send(OrderWaypoint waypoint, Phase phase)
  {
    var helper = Helper();
    OrderCapture.Dispatching = true;
    try
    {
      helper.SetWaypointOrder(waypoint, Dispatcher.SpeedFor(_locomotive));
    }
    finally
    {
      OrderCapture.Dispatching = false;
    }
    _locationString = waypoint.LocationString;
    _phase = phase;
    _phaseStarted = Time.time;
    _moved = false;
    if (phase == Phase.Leaving) Remember();
  }

  private bool OnBridge()
  {
    var bridge = _site.Bridge;
    return _cars.All(car => car.LocationA.segment == bridge || car.LocationB.segment == bridge);
  }

  private bool Touching()
  {
    var bridge = _site.Bridge;
    return _cars.Any(car => car.LocationA.segment == bridge || car.LocationB.segment == bridge) || _site.Turntable.TryGetCarBlockingMovement(out _);
  }

  private float CenterError()
  {
    var graph = Graph.Shared;
    var endA = graph.GetPosition(_site.Turntable.BridgeLocation(TrackSegment.End.A, 0f));
    var endB = graph.GetPosition(_site.Turntable.BridgeLocation(TrackSegment.End.B, 0f));
    var axis = (endB - endA).ZeroY();
    if (axis.sqrMagnitude < 0.01f) return 0f;
    var first = graph.GetPosition(_cars[0].LocationA);
    var last = graph.GetPosition(_cars[_cars.Count - 1].LocationB);
    return Vector3.Dot(((first + last) / 2f - (endA + endB) / 2f).ZeroY(), axis.normalized);
  }

  private string Blocking() => _site.Turntable.TryGetCarBlockingMovement(out var car) && car != null ? $": {car.DisplayName} is over its edge" : "";

  private void Fail(string problem) => Problem ??= problem;

  private AutoEngineerOrdersHelper Helper() => new(_locomotive, new AutoEngineerPersistence(_locomotive.KeyValueObject));

  private enum Phase
  {
    Lining,
    Entering,
    Turning,
    Leaving
  }
}
