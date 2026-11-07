using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Model.AI;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class Maneuvers
{
  internal const int AllCars = 0;

  internal static int ReleaseHandbrakes(Car locomotive)
  {
    var released = 0;
    foreach (var car in locomotive.EnumerateCoupled())
    {
      if (car.air == null || !car.air.handbrakeApplied) continue;
      car.SetHandbrake(false);
      released++;
    }
    return released;
  }

  internal static void ConnectAir(Car locomotive)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    for (var i = 0; i < cars.Count - 1; i++)
    {
      var front = cars[i];
      var back = cars[i + 1];
      if (!front[Car.LogicalEnd.B].IsAirConnected) StateManager.ApplyLocal(new SetGladhandsConnected(front.id, back.id, true));
      SetAnglecock(front, Car.LogicalEnd.B, true);
      SetAnglecock(back, Car.LogicalEnd.A, true);
    }
    if (cars.Count == 0) return;
    SetAnglecock(cars[0], Car.LogicalEnd.A, false);
    SetAnglecock(cars[cars.Count - 1], Car.LogicalEnd.B, false);
  }

  internal static (int Front, int Rear) CarsAround(BaseLocomotive locomotive)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var index = cars.IndexOf(locomotive);
    var atA = cars.Take(Mathf.Max(index, 0)).Count(IsCar);
    var atB = cars.Skip(index + 1).Count(IsCar);
    return FrontAtB(locomotive) ? (atB, atA) : (atA, atB);
  }

  internal static bool PlanCut(BaseLocomotive locomotive, int count, CutSide cutSide, out CutPlan plan, out string problem)
  {
    plan = null;
    problem = null;
    if (count < 0)
    {
      problem = "Nothing to leave";
      return false;
    }
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var index = cars.IndexOf(locomotive);
    var cutAtB = cutSide switch
    {
      CutSide.Front => FrontAtB(locomotive),
      CutSide.Rear => !FrontAtB(locomotive),
      _ => cars.Skip(index + 1).Count(IsCar) >= cars.Take(index).Count(IsCar)
    };
    var side = cutAtB ? Enumerable.Reverse(cars.Skip(index + 1)).ToList() : cars.Take(index).ToList();
    var leaving = new List<Car>();
    foreach (var car in side)
    {
      if (!IsCar(car)) break;
      leaving.Add(car);
      if (leaving.Count == count) break;
    }
    if (leaving.Count == 0)
    {
      problem = side.Count > 0 ? "No cars to leave: a locomotive is at that end" : "No cars to leave";
      return false;
    }
    if (count != AllCars && leaving.Count < count)
    {
      problem = $"Only {leaving.Count} {(leaving.Count == 1 ? "car" : "cars")} to leave";
      return false;
    }
    var cutCar = leaving[leaving.Count - 1];
    var cutIndex = cars.IndexOf(cutCar);
    plan = new CutPlan(cutAtB ? cars[cutIndex - 1] : cars[cutIndex + 1], cutCar, cutAtB, leaving[0]);
    return true;
  }

  internal static bool PlanKeep(BaseLocomotive locomotive, int count, CutSide cutSide, out CutPlan plan, out string problem)
  {
    plan = null;
    problem = null;
    if (count < 1)
    {
      problem = "Nothing to keep";
      return false;
    }
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var index = cars.IndexOf(locomotive);
    var cutAtB = cutSide switch
    {
      CutSide.Front => FrontAtB(locomotive),
      CutSide.Rear => !FrontAtB(locomotive),
      _ => cars.Skip(index + 1).Count(IsCar) >= cars.Take(index).Count(IsCar)
    };
    var outward = cutAtB ? cars.Skip(index + 1).ToList() : Enumerable.Reverse(cars.Take(index)).ToList();
    var at = 0;
    while (at < outward.Count && !IsCar(outward[at])) at++;
    var kept = 0;
    while (at < outward.Count && kept < count)
    {
      if (IsCar(outward[at])) kept++;
      at++;
    }
    if (at >= outward.Count)
    {
      problem = kept == 0 ? "No cars on that side" : $"Nothing to leave: only {kept} {(kept == 1 ? "car" : "cars")} on that side";
      return false;
    }
    plan = new CutPlan(at > 0 ? outward[at - 1] : locomotive, outward[at], cutAtB);
    return true;
  }

  internal static void ExecuteCut(CutPlan plan)
  {
    var keptEnd = plan.CutAtB ? Car.LogicalEnd.B : Car.LogicalEnd.A;
    var cutEnd = plan.CutAtB ? Car.LogicalEnd.A : Car.LogicalEnd.B;
    if (plan.CutCar.air != null && !plan.CutCar.air.handbrakeApplied) plan.CutCar.SetHandbrake(true);
    SetAnglecock(plan.Kept, keptEnd, false);
    SetAnglecock(plan.CutCar, cutEnd, false);
    if (plan.Kept[keptEnd].IsAirConnected)
    {
      var front = plan.CutAtB ? plan.Kept : plan.CutCar;
      var back = plan.CutAtB ? plan.CutCar : plan.Kept;
      StateManager.ApplyLocal(new SetGladhandsConnected(front.id, back.id, false));
    }
    plan.Kept.ApplyEndGearChange(keptEnd, Car.EndGearStateKey.CutLever, 1f);
  }

  internal sealed class CutPlan
  {
    public CutPlan(Car kept, Car cutCar, bool cutAtB, Car far = null)
    {
      Kept = kept;
      CutCar = cutCar;
      CutAtB = cutAtB;
      Far = far;
    }

    public Car Far { get; }
    public Car Kept { get; }
    public Car CutCar { get; }
    public bool CutAtB { get; }
  }

  private static bool FrontAtB(Car locomotive) => locomotive.EndToLogical(Car.End.F) == Car.LogicalEnd.B;

  internal static CutSide SideOf(BaseLocomotive locomotive, Car car)
  {
    var cars = locomotive.EnumerateCoupled(Car.LogicalEnd.A).ToList();
    var atB = cars.IndexOf(car) > cars.IndexOf(locomotive);
    return atB == FrontAtB(locomotive) ? CutSide.Front : CutSide.Rear;
  }

  internal static WaypointEnd EndAwayFrom(BaseLocomotive locomotive, CutSide side)
  {
    var front = FrontAtB(locomotive) ? WaypointEnd.B : WaypointEnd.A;
    var back = front == WaypointEnd.A ? WaypointEnd.B : WaypointEnd.A;
    return side == CutSide.Rear ? front : back;
  }

  private static bool IsCar(Car car) => car is not BaseLocomotive && !car.CarType.IsTender;

  private static void SetAnglecock(Car car, Car.LogicalEnd end, bool open)
  {
    var setting = car[end].AnglecockSetting;
    if (open ? setting > 0.99f : setting < 0.01f) return;
    car.ApplyEndGearChange(end, Car.EndGearStateKey.Anglecock, open ? 1f : 0f);
  }
}
