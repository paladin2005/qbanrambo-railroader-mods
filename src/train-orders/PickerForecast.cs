using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using Model.AI;
using UI.Waypoints;
using UnityEngine;

namespace QbanRambo.TrainOrders;

[HarmonyPatch(typeof(AutoEngineerDestinationPicker), "RefreshConsist")]
internal static class PickerForecast
{
  private const float CacheSeconds = 1f;

  private static BaseLocomotive _cachedFor;
  private static List<Car> _cachedCars;
  private static float _cachedAt = float.MinValue;
  private static bool _logged;

  internal static void Reset()
  {
    _cachedFor = null;
    _cachedCars = null;
    _cachedAt = float.MinValue;
  }

  private static void Postfix(BaseLocomotive ____locomotive, List<Car> ____consist, ref float ____trainLength)
  {
    try
    {
      if (____locomotive == null || ____consist == null || !OrdersWindow.IsOpenFor(____locomotive)) return;
      var cars = FutureCars(____locomotive);
      if (cars == null || cars.Count == 0) return;
      ____consist.Clear();
      ____consist.AddRange(cars);
      ____trainLength = AutoEngineerPlanner.CalculateTotalLength(____consist);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      TrainOrdersMod.Log?.Error(exception, "Couldn't show the future train in the waypoint picker");
    }
  }

  private static List<Car> FutureCars(BaseLocomotive locomotive)
  {
    if (_cachedFor == locomotive && Time.unscaledTime - _cachedAt < CacheSeconds) return _cachedCars;
    _cachedFor = locomotive;
    _cachedAt = Time.unscaledTime;
    _cachedCars = null;
    var orders = OrderList.Read(locomotive);
    if (orders.Steps.Count == 0 || orders.Finished) return null;
    var run = Forecast.Run(locomotive, orders);
    var cars = run.FinalTrain.Where(car => car.Car != null).Select(car => car.Car).ToList();
    if (run.FrontAtB) cars.Reverse();
    if (!cars.Contains(locomotive)) return null;
    _cachedCars = cars;
    return cars;
  }
}
