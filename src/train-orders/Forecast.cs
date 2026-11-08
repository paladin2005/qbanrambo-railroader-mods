using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using Model.AI.Waypoints;
using Track;
using Track.Search;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal sealed class ForecastCar
{
  private ForecastCar(Car car, string id)
  {
    Car = car;
    Id = id;
  }

  public Car Car { get; }
  public string Id { get; }
  public bool IsUnknown => Car == null;
  public bool IsCar => Car == null || (Car is not BaseLocomotive && !Car.CarType.IsTender);

  public static ForecastCar Of(Car car) => new(car, car.id);

  public static ForecastCar Unknown(int index) => new(null, $"?{index}");
}

internal sealed class ForecastResult
{
  public List<ForecastCar> Before { get; set; } = new();
  public List<ForecastCar> After { get; set; } = new();
  public HashSet<string> Added { get; } = new();
  public HashSet<string> Leaving { get; } = new();
  public string Note { get; set; }
  public bool Problem { get; set; }
  public bool Guess { get; set; }
}

internal sealed class CarSpot
{
  public CarSpot(List<Location> path)
  {
    Path = path;
  }

  public List<Location> Path { get; }
  public Location Head => Path[0];

  public CarSpot Reversed() => new(Enumerable.Reverse(Path).Select(location => location.Flipped()).ToList());
}

internal sealed class GhostState
{
  private string _signature;

  public List<(ForecastCar Car, CarSpot Spot)> Train { get; } = new();
  public List<(ForecastCar Car, CarSpot Spot)> Left { get; } = new();

  public string Signature => _signature ??= string.Join("|", Train.Select(entry => Key("t", entry)).Concat(Left.Select(entry => Key("l", entry))));

  private static string Key(string kind, (ForecastCar Car, CarSpot Spot) entry)
  {
    var head = entry.Spot.Head;
    return $"{kind}:{entry.Car.Id}:{head.segment?.id}:{Mathf.Round(head.distance * 10f)}:{head.end}";
  }
}

internal sealed class ForecastRun
{
  public Dictionary<int, ForecastResult> Results { get; } = new();
  public Dictionary<int, GhostState> States { get; } = new();
  public List<ForecastCar> FinalTrain { get; set; } = new();
  public bool FrontAtB { get; set; }
}

internal static class Forecast
{
  private static bool _warned;

  internal static bool Changes(OrderStep step) => step.IsCouple || step.Kind is StepKind.Cut or StepKind.PickUp or StepKind.CoupleBack or StepKind.CoupleTo or StepKind.RunAround;

  internal static ForecastRun Run(BaseLocomotive locomotive, OrderList orders)
  {
    var run = new ForecastRun();
    if (locomotive == null || orders.Finished) return run;
    var state = new State(locomotive, orders.LeftCarId, orders.Cuts);
    for (var i = orders.Current; i < orders.Steps.Count; i++)
    {
      var step = orders.Steps[i];
      var result = new ForecastResult { Before = state.Train.ToList() };
      try
      {
        state.Apply(step, result, i == orders.Current);
      }
      catch (Exception exception)
      {
        result.Problem = true;
        result.Note = "Can't tell what this order will do";
        state.Lose();
        if (!_warned) TrainOrdersMod.Log?.Warning($"Couldn't forecast order {i + 1}: {exception.Message}");
        _warned = true;
      }
      result.After = state.Train.ToList();
      if (Changes(step)) run.Results[i] = result;
      run.States[i] = state.Snapshot();
    }
    run.FinalTrain = state.Train.ToList();
    run.FrontAtB = state.FrontAtB;
    return run;
  }

  internal const string FutureCarsKey = "qbanrambo.train-orders.future-cars";

  internal static readonly Func<object, int> FutureCarsHook = target => target is BaseLocomotive locomotive ? FutureCars(locomotive) : -1;

  internal static int FutureCars(BaseLocomotive locomotive)
  {
    var train = FinalTrain(locomotive);
    return train == null ? -1 : train.Count(car => car.IsCar);
  }

  internal static (int Front, int Rear) CarsAroundLater(BaseLocomotive locomotive)
  {
    var train = FinalTrain(locomotive);
    var index = train?.FindIndex(car => car.Car == locomotive) ?? -1;
    if (index < 0) return Maneuvers.CarsAround(locomotive);
    return (train.Take(index).Count(car => car.IsCar), train.Skip(index + 1).Count(car => car.IsCar));
  }

  private static List<ForecastCar> FinalTrain(BaseLocomotive locomotive)
  {
    try
    {
      var orders = OrderList.Read(locomotive);
      if (orders.Finished) return null;
      return Run(locomotive, orders).FinalTrain;
    }
    catch (Exception exception)
    {
      if (!_warned) TrainOrdersMod.Log?.Warning($"Couldn't forecast the train after the orders: {exception.Message}");
      _warned = true;
      return null;
    }
  }

  internal static string Name(string carId) => TryGetCar(carId, out var car) ? car.DisplayName : "a car";

  private static bool TryGetCar(string id, out Car car)
  {
    car = null;
    return !string.IsNullOrEmpty(id) && TrainController.Shared != null && TrainController.Shared.TryGetCarForId(id, out car) && car != null;
  }

  private sealed class Group
  {
    public Group(List<ForecastCar> cars, List<CarSpot> spots, bool front)
    {
      Cars = cars;
      Spots = spots;
      Front = front;
    }

    public List<ForecastCar> Cars { get; }
    public List<CarSpot> Spots { get; }
    public bool Front { get; }
  }

  private sealed class State
  {
    private const float CouplerGap = 1.04f;
    private const float CouplerOverhang = 0.5f;
    private const float UnknownLength = 12.5f;
    private const float SampleStep = 2f;
    private const float ProbeDistance = 25f;

    private readonly BaseLocomotive _locomotive;
    private readonly bool _frontAtB;
    private readonly IGraphView _view;
    private readonly Dictionary<string, Group> _standing = new();
    private readonly HashSet<string> _placed = new();
    private List<CarSpot> _spots;
    private bool _moved;
    private string _leftCarId;
    private readonly Dictionary<string, string> _leftBy;
    private int _unknown;

    public State(BaseLocomotive locomotive, string leftCarId, Dictionary<string, string> leftBy)
    {
      _leftBy = new Dictionary<string, string>(leftBy);
      _locomotive = locomotive;
      _frontAtB = locomotive.EndToLogical(Car.End.F) == Car.LogicalEnd.B;
      _view = Graph.Shared?.Current;
      var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
      if (_frontAtB) cars.Reverse();
      Train = cars.Select(ForecastCar.Of).ToList();
      _placed.UnionWith(Train.Select(car => car.Id));
      _leftCarId = leftCarId;
      try
      {
        _spots = RealSpots();
      }
      catch (Exception)
      {
        _spots = null;
      }
    }

    public List<ForecastCar> Train { get; }

    public bool FrontAtB => _frontAtB;

    public void Apply(OrderStep step, ForecastResult result, bool current)
    {
      switch (step.Kind)
      {
        case StepKind.Cut:
          Cut(step, result, current);
          break;
        case StepKind.PickUp:
          PickUp(step, result);
          break;
        case StepKind.CoupleBack:
          CoupleBack(result);
          break;
        case StepKind.CoupleTo:
          CoupleTo(step, result);
          break;
        case StepKind.RunAround:
          RunAround(step, result, current);
          break;
        case StepKind.Move when step.IsCouple:
          Couple(step, result);
          break;
        case StepKind.Move:
          Place(step);
          break;
        case StepKind.Station:
        case StepKind.Refuel:
        case StepKind.Turntable:
          Lose();
          break;
      }
    }

    public void Lose()
    {
      _spots = null;
      _moved = true;
    }

    public GhostState Snapshot()
    {
      var ghost = new GhostState();
      if (_moved && _spots != null)
      {
        for (var i = 0; i < Train.Count && i < _spots.Count; i++)
        {
          if (_spots[i] != null) ghost.Train.Add((Train[i], _spots[i]));
        }
      }
      foreach (var group in _standing.Values.Distinct())
      {
        if (group.Spots == null) continue;
        for (var i = 0; i < group.Cars.Count && i < group.Spots.Count; i++)
        {
          if (group.Spots[i] != null) ghost.Left.Add((group.Cars[i], group.Spots[i]));
        }
      }
      return ghost;
    }

    private void Couple(OrderStep step, ForecastResult result)
    {
      var waypoint = step.Waypoint.Value;
      var targetId = waypoint.CoupleToCarId;
      if (Train.Any(car => car.Id == targetId))
      {
        result.Note = $"{Name(targetId)} is already in the train";
        return;
      }
      if (!TryTake(targetId, waypoint.CoupleToCarEnd, out var outward, out var spots, out _))
      {
        result.Problem = true;
        result.Note = $"{Name(targetId)} isn't on the map";
        Lose();
        return;
      }
      var end = step.CabArrivingEnd(_locomotive);
      bool front;
      if (end == WaypointEnd.A) front = true;
      else if (end == WaypointEnd.B) front = false;
      else
      {
        front = NearerFront(outward[0]);
        result.Guess = true;
      }
      if (step.Leave.HasValue)
      {
        Attach(outward, spots, front, Maneuvers.AllCars, result);
        LeaveOwn(outward, step.Leave.Value, front, result, step.Id);
      }
      else
      {
        Attach(outward, spots, front, step.Count, result, step.Id);
      }
    }

    private void CoupleBack(ForecastResult result)
    {
      if (_leftCarId == null)
      {
        result.Problem = true;
        result.Note = "No cars left behind to couple back to";
        return;
      }
      CoupleToCar(_leftCarId, result);
    }

    private void CoupleTo(OrderStep step, ForecastResult result)
    {
      if (step.From == null || !_leftBy.TryGetValue(step.From, out var carId))
      {
        result.Problem = true;
        result.Note = "That order hasn't left its cars yet";
        return;
      }
      CoupleToCar(carId, result);
    }

    private void CoupleToCar(string carId, ForecastResult result)
    {
      if (Train.Any(car => car.Id == carId))
      {
        result.Note = $"{Name(carId)} is already in the train";
        return;
      }
      if (!TryTake(carId, null, out var outward, out var spots, out var cutFront))
      {
        result.Problem = true;
        result.Note = "The cars it left are gone";
        Lose();
        return;
      }
      result.Guess = true;
      Attach(outward, spots, cutFront ?? NearerFront(outward[0]), Maneuvers.AllCars, result);
    }

    private void PickUp(OrderStep step, ForecastResult result)
    {
      if (step.CutCarId != null && Train.All(car => car.Id != step.CutCarId) && TryTake(step.CutCarId, null, out var outward, out var spots, out var cutFront))
      {
        result.Guess = true;
        Attach(outward, spots, cutFront ?? NearerFront(outward[0]), step.Count, result, step.Id);
        return;
      }
      result.Guess = true;
      result.Note = step.Count == Maneuvers.AllCars ? "Picks up all the standing cars it finds when the order runs" : "Picks up the nearest standing cars it finds when the order runs";
      var count = step.Count == Maneuvers.AllCars ? 1 : step.Count;
      var cars = Enumerable.Range(0, count).Select(_ => ForecastCar.Unknown(_unknown++)).ToList();
      foreach (var car in cars) result.Added.Add(car.Id);
      Train.AddRange(cars);
      Lose();
    }

    private void Cut(OrderStep step, ForecastResult result, bool current)
    {
      if (current && step.CutCarId != null && Train.All(car => car.Id != step.CutCarId)) return;
      var index = Train.FindIndex(car => car.Id == _locomotive.id);
      if (index < 0) return;
      var ahead = Train.Take(index).ToList();
      var behind = Train.Skip(index + 1).ToList();
      var cutFront = step.Side switch
      {
        CutSide.Front => true,
        CutSide.Rear => false,
        _ => AutoFront(ahead, behind)
      };
      var outward = cutFront ? Enumerable.Reverse(ahead).ToList() : behind;
      var leaving = step.Keep ? KeepLeaving(outward, step.Count, out var problem) : CutLeaving(outward, step.Count, out problem);
      if (leaving == null)
      {
        result.Problem = true;
        result.Note = problem;
        return;
      }
      List<CarSpot> spots = null;
      if (_spots != null)
      {
        spots = leaving.Select(car => _spots[Train.IndexOf(car)]).Select(spot => cutFront ? spot?.Reversed() : spot).ToList();
      }
      foreach (var car in leaving)
      {
        result.Leaving.Add(car.Id);
        RemoveFromTrain(car);
      }
      Stand(leaving, spots, cutFront);
      _leftCarId = leaving[0].Id;
      if (step.Id != null) _leftBy[step.Id] = leaving[0].Id;
    }

    private void LeaveOwn(List<ForecastCar> group, int leave, bool front, ForecastResult result, string stepId)
    {
      var fromEnd = front ? Train.ToList() : Enumerable.Reverse(Train).ToList();
      var own = fromEnd.Skip(group.Count).TakeWhile(car => car.IsCar).Take(leave).ToList();
      if (leave == OrderStep.LeaveAll && own.Count > 0) leave = own.Count;
      if (own.Count < leave || fromEnd.Count <= group.Count + leave)
      {
        result.Problem = true;
        result.Note = own.Count == 0 ? "No cars of the train to leave next to the coupling" : $"Only {Cars(own.Count)} of the train to leave next to the coupling";
        return;
      }
      var leaving = Enumerable.Reverse(fromEnd.Take(group.Count + leave)).ToList();
      List<CarSpot> spots = null;
      if (_spots != null)
      {
        spots = leaving.Select(car => _spots[Train.IndexOf(car)]).Select(spot => front ? spot?.Reversed() : spot).ToList();
      }
      foreach (var car in group) result.Added.Remove(car.Id);
      foreach (var car in own) result.Leaving.Add(car.Id);
      foreach (var car in leaving) RemoveFromTrain(car);
      Stand(leaving, spots, front);
      _leftCarId = leaving[0].Id;
      if (stepId != null) _leftBy[stepId] = leaving[0].Id;
    }

    private void RunAround(OrderStep step, ForecastResult result, bool current)
    {
      var index = Train.FindIndex(car => car.Id == _locomotive.id);
      if (index < 0) return;
      var front = step.Side != CutSide.Rear;
      var farIndex = step.FarId != null ? Train.FindIndex(car => car.Id == step.FarId) : -1;
      if (current && farIndex >= 0 && (farIndex > index) == front)
      {
        result.Note = "Already around the cars";
        return;
      }
      List<ForecastCar> leaving;
      if (current && step.FarId != null && farIndex < 0 && TryGetCar(step.FarId, out var farCar))
      {
        var group = farCar.EnumerateCoupled().ToList();
        if (group.IndexOf(farCar) != 0) group.Reverse();
        leaving = group.Select(ForecastCar.Of).ToList();
        if (!front) leaving.Reverse();
      }
      else
      {
        var outward = front ? Enumerable.Reverse(Train.Take(index)).ToList() : Train.Skip(index + 1).ToList();
        leaving = CutLeaving(outward, Maneuvers.AllCars, out var problem);
        if (leaving == null)
        {
          result.Problem = true;
          result.Note = problem;
          return;
        }
        if (front) leaving.Reverse();
        foreach (var car in leaving) result.Leaving.Add(car.Id);
      }
      Lose();
      foreach (var car in leaving) RemoveFromTrain(car);
      if (front) Train.AddRange(leaving);
      else Train.InsertRange(0, leaving);
      _placed.UnionWith(leaving.Select(car => car.Id));
      result.Note = $"Leaves {Cars(leaving.Count)} and couples to their far end";
    }

    private void RemoveFromTrain(ForecastCar car)
    {
      var index = Train.IndexOf(car);
      if (index < 0) return;
      Train.RemoveAt(index);
      if (_spots != null && index < _spots.Count) _spots.RemoveAt(index);
    }

    private bool AutoFront(List<ForecastCar> ahead, List<ForecastCar> behind)
    {
      var aheadCars = ahead.Count(car => car.IsCar);
      var behindCars = behind.Count(car => car.IsCar);
      var atA = _frontAtB ? behindCars : aheadCars;
      var atB = _frontAtB ? aheadCars : behindCars;
      var cutAtB = atB >= atA;
      return cutAtB == _frontAtB;
    }

    private static List<ForecastCar> CutLeaving(List<ForecastCar> outward, int count, out string problem)
    {
      problem = null;
      var fromEnd = Enumerable.Reverse(outward).ToList();
      var leaving = new List<ForecastCar>();
      foreach (var car in fromEnd)
      {
        if (!car.IsCar) break;
        leaving.Add(car);
        if (leaving.Count == count) break;
      }
      if (leaving.Count == 0)
      {
        problem = fromEnd.Count > 0 ? "No cars to leave: a locomotive is at that end" : "No cars to leave";
        return null;
      }
      if (count != Maneuvers.AllCars && leaving.Count < count)
      {
        problem = $"Only {Cars(leaving.Count)} to leave";
        return null;
      }
      leaving.Reverse();
      return leaving;
    }

    private static List<ForecastCar> KeepLeaving(List<ForecastCar> outward, int count, out string problem)
    {
      problem = null;
      if (count < 1)
      {
        problem = "Nothing to keep";
        return null;
      }
      var at = 0;
      while (at < outward.Count && !outward[at].IsCar) at++;
      var kept = 0;
      while (at < outward.Count && kept < count)
      {
        if (outward[at].IsCar) kept++;
        at++;
      }
      if (at >= outward.Count)
      {
        problem = kept == 0 ? "No cars on that side" : $"Nothing to leave: only {Cars(kept)} on that side";
        return null;
      }
      return outward.Skip(at).ToList();
    }

    private void Attach(List<ForecastCar> outward, List<CarSpot> outwardSpots, bool front, int take, ForecastResult result, string stepId = null)
    {
      var keptCount = take != Maneuvers.AllCars && outward.Count > take ? take : outward.Count;
      var kept = outward.Take(keptCount).ToList();
      var keptSpots = outwardSpots?.Take(keptCount).ToList();
      if (keptCount < outward.Count)
      {
        var rest = outward.Skip(keptCount).ToList();
        Stand(rest, outwardSpots?.Skip(keptCount).ToList(), front);
        _leftCarId = rest[0].Id;
        if (stepId != null) _leftBy[stepId] = rest[0].Id;
      }
      foreach (var car in kept)
      {
        result.Added.Add(car.Id);
        _standing.Remove(car.Id);
        _placed.Add(car.Id);
      }
      List<CarSpot> trainSpots = null;
      try
      {
        trainSpots = CoupledSpots(keptSpots, front);
      }
      catch (Exception)
      {
        trainSpots = null;
      }
      if (front) Train.InsertRange(0, Enumerable.Reverse(kept));
      else Train.AddRange(kept);
      _spots = trainSpots;
      _moved = true;
    }

    private List<CarSpot> CoupledSpots(List<CarSpot> keptSpots, bool front)
    {
      if (_view == null || keptSpots == null || keptSpots.Count == 0 || keptSpots[0] == null) return null;
      var start = _view.LocationByMoving(keptSpots[0].Head.Flipped(), CouplerGap, false, true);
      var order = front ? Train.ToList() : Enumerable.Reverse(Train).ToList();
      var own = Walk(start, order);
      if (!front)
      {
        own.Reverse();
        own = own.Select(spot => spot?.Reversed()).ToList();
      }
      var coupled = front ? Enumerable.Reverse(keptSpots).Select(spot => spot?.Reversed()).ToList() : keptSpots;
      return front ? coupled.Concat(own).ToList() : own.Concat(coupled).ToList();
    }

    private void Place(OrderStep step)
    {
      var previous = _spots;
      _spots = null;
      _moved = true;
      if (_view == null || !step.Waypoint.HasValue) return;
      var waypoint = step.Waypoint.Value;
      var mark = Graph.Shared.ResolveLocationString(waypoint.LocationString);
      var side = waypoint.BodyToward != BodyToward.Unspecified ? WaypointPose.ToSegmentEnd(waypoint.BodyToward) : GuessSide(mark, previous);
      var end = step.CabArrivingEnd(_locomotive);
      var frontArrives = end == WaypointEnd.A || (end != WaypointEnd.B && FrontLeads(mark, previous));
      var start = WaypointPose.IntoBody(mark, side);
      if (frontArrives)
      {
        _spots = Walk(start, Train);
        return;
      }
      var spots = Walk(start, Enumerable.Reverse(Train).ToList());
      spots.Reverse();
      _spots = spots.Select(spot => spot?.Reversed()).ToList();
    }

    private TrackSegment.End GuessSide(Location mark, List<CarSpot> previous)
    {
      var center = Center(previous);
      if (!center.HasValue) return TrackSegment.End.B;
      var best = TrackSegment.End.B;
      var bestDistance = float.MaxValue;
      foreach (var side in new[] { TrackSegment.End.A, TrackSegment.End.B })
      {
        Location probe;
        try
        {
          probe = _view.LocationByMoving(WaypointPose.IntoBody(mark, side), ProbeDistance, false, true);
        }
        catch (Exception)
        {
          continue;
        }
        var distance = Vector3.Distance(probe.GetPosition(), center.Value);
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        best = side;
      }
      return best;
    }

    private static bool FrontLeads(Location mark, List<CarSpot> previous)
    {
      if (previous == null || previous.Count == 0 || previous[0] == null || previous[previous.Count - 1] == null) return true;
      var target = mark.GetPosition();
      var frontPath = previous[0].Path;
      var rearPath = previous[previous.Count - 1].Path;
      return Vector3.Distance(frontPath[0].GetPosition(), target) <= Vector3.Distance(rearPath[rearPath.Count - 1].GetPosition(), target);
    }

    private static Vector3? Center(List<CarSpot> spots)
    {
      var known = spots?.Where(spot => spot != null).ToList();
      if (known == null || known.Count == 0) return null;
      var middle = known[known.Count / 2].Path;
      return middle[middle.Count / 2].GetPosition();
    }

    private void Stand(List<ForecastCar> cars, List<CarSpot> spots, bool front)
    {
      var group = new Group(cars, spots, front);
      foreach (var car in cars)
      {
        _standing[car.Id] = group;
        _placed.Add(car.Id);
      }
    }

    private bool TryTake(string targetId, Car.End? coupleEnd, out List<ForecastCar> outward, out List<CarSpot> spots, out bool? cutFront)
    {
      outward = null;
      spots = null;
      cutFront = null;
      if (_standing.TryGetValue(targetId, out var group))
      {
        var index = group.Cars.FindIndex(car => car.Id == targetId);
        var cars = group.Cars;
        var fromOuter = index > 0 && index == cars.Count - 1;
        var taken = fromOuter ? Enumerable.Reverse(cars).ToList() : cars.Skip(index).ToList();
        if (group.Spots != null) spots = fromOuter ? Enumerable.Reverse(group.Spots).Select(spot => spot?.Reversed()).ToList() : group.Spots.Skip(index).ToList();
        outward = taken;
        foreach (var car in cars) _standing.Remove(car.Id);
        if (!fromOuter && index > 0) Stand(cars.Take(index).ToList(), group.Spots?.Take(index).ToList(), group.Front);
        cutFront = group.Front;
        return true;
      }
      if (!TryGetCar(targetId, out var target) || target.IsInBardo) return false;
      var real = target.EnumerateCoupled(Car.LogicalEnd.A).Where(car => !_placed.Contains(car.id)).ToList();
      var at = real.IndexOf(target);
      if (at < 0) return false;
      List<Car> ordered;
      Car.End end;
      if (coupleEnd.HasValue && at > 0 && at < real.Count - 1)
      {
        end = coupleEnd.Value;
        ordered = target.EndToLogical(end) == Car.LogicalEnd.B ? Enumerable.Reverse(real.Take(at + 1)).ToList() : real.Skip(at).ToList();
      }
      else if (real.Count == 1)
      {
        end = coupleEnd ?? NearerEnd(target);
        ordered = real;
      }
      else if (at == 0)
      {
        end = target.LogicalToEnd(Car.LogicalEnd.A);
        ordered = real;
      }
      else if (at == real.Count - 1)
      {
        end = target.LogicalToEnd(Car.LogicalEnd.B);
        ordered = Enumerable.Reverse(real).ToList();
      }
      else
      {
        end = target.LogicalToEnd(Car.LogicalEnd.A);
        ordered = real.Skip(at).ToList();
      }
      outward = ordered.Select(ForecastCar.Of).ToList();
      try
      {
        if (_view != null)
        {
          spots = Walk(IntoCar(target, end), outward);
        }
      }
      catch (Exception)
      {
        spots = null;
      }
      return true;
    }

    private Car.End NearerEnd(Car target)
    {
      var center = Center(_spots);
      if (!center.HasValue || _view == null) return Car.End.F;
      var front = PosedWaypoint.CouplerMark(_view, target, Car.End.F).GetPosition();
      var rear = PosedWaypoint.CouplerMark(_view, target, Car.End.R).GetPosition();
      return Vector3.Distance(front, center.Value) <= Vector3.Distance(rear, center.Value) ? Car.End.F : Car.End.R;
    }

    private bool NearerFront(ForecastCar target)
    {
      if (target.Car == null) return false;
      var position = target.Car.LocationF.GetPosition();
      if (_spots != null && _spots.Count > 0 && _spots[0] != null && _spots[_spots.Count - 1] != null)
      {
        var rearPath = _spots[_spots.Count - 1].Path;
        return Vector3.Distance(position, _spots[0].Head.GetPosition()) < Vector3.Distance(position, rearPath[rearPath.Count - 1].GetPosition());
      }
      var first = Train.FirstOrDefault(car => car.Car != null)?.Car;
      var last = Train.LastOrDefault(car => car.Car != null)?.Car;
      if (first == null || last == null || first == last) return false;
      return Vector3.Distance(position, first.LocationF.GetPosition()) < Vector3.Distance(position, last.LocationF.GetPosition());
    }

    private List<CarSpot> RealSpots()
    {
      if (_view == null || Train.Count == 0 || Train[0].Car == null) return null;
      var front = Train[0].Car;
      var outer = Car.End.F;
      if (Train.Count > 1 && Train[1].Car != null)
      {
        var next = Train[1].Car.LocationF.GetPosition();
        var markF = PosedWaypoint.CouplerMark(_view, front, Car.End.F).GetPosition();
        var markR = PosedWaypoint.CouplerMark(_view, front, Car.End.R).GetPosition();
        outer = Vector3.Distance(markF, next) >= Vector3.Distance(markR, next) ? Car.End.F : Car.End.R;
      }
      return Walk(IntoCar(front, outer), Train);
    }

    private Location IntoCar(Car car, Car.End end)
    {
      var mark = PosedWaypoint.CouplerMark(_view, car, end);
      return _view.LocationByMoving(mark.Flipped(), CouplerOverhang, false, true);
    }

    private List<CarSpot> Walk(Location start, IList<ForecastCar> cars)
    {
      var lengths = cars.Select(car => car.Car != null ? car.Car.carLength : UnknownLength).ToList();
      var total = lengths.Sum() + CouplerGap * Mathf.Max(0, lengths.Count - 1);
      var spans = new List<WalkSegment>();
      TrackPlacement.Walk(_view, start, total, null, spans);
      var spots = new List<CarSpot>();
      var at = 0f;
      foreach (var length in lengths)
      {
        spots.Add(Fill(spans, at, at + length));
        at += length + CouplerGap;
      }
      return spots;
    }

    private static CarSpot Fill(List<WalkSegment> spans, float from, float to)
    {
      var points = new List<Location>();
      for (var d = from; ; d += SampleStep)
      {
        var clamped = Mathf.Min(d, to);
        if (!TryAt(spans, clamped, out var location)) break;
        points.Add(location);
        if (clamped >= to) break;
      }
      return points.Count >= 2 ? new CarSpot(points) : null;
    }

    private static bool TryAt(List<WalkSegment> spans, float distance, out Location location)
    {
      foreach (var span in spans)
      {
        if (distance < span.EntryDistance - 0.001f || distance > span.ExitDistance + 0.001f) continue;
        location = span.EntryLocation.Moving(distance - span.EntryDistance);
        return true;
      }
      location = default;
      return false;
    }

    private static string Cars(int count) => count == 1 ? "1 car" : $"{count} cars";
  }
}
