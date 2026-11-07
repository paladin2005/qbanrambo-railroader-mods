using System;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.AI;
using UI.Common;
using UI.EngineControls;

namespace QbanRambo.TrainOrders;

internal static class OrderCapture
{
  private static readonly FieldInfo LocomotiveField = AccessTools.Field(typeof(AutoEngineerOrdersHelper), "_locomotive");

  private const string GotoDeadEndChoiceKey = "qbanrambo.goto.dead-end-choice";
  private const string GotoSmartEntrance = "smart";
  private const string GotoNormalEntrance = "normal";
  private const string GotoStationKey = "qbanrambo.goto.station";
  private const string GotoRefuelKey = "qbanrambo.goto.refuel";
  private const string GotoDeliverKey = "qbanrambo.goto.deliver";

  internal const string TurntableHookKey = "qbanrambo.train-orders.turntable";
  internal const string RunAroundHookKey = "qbanrambo.train-orders.runaround";
  internal const string WindowOpenKey = "qbanrambo.train-orders.window-open";
  private const string CapturedKey = "qbanrambo.train-orders.captured";

  internal static bool Dispatching;

  internal static readonly Func<object, string, int, bool> TurntableHook = CaptureTurntable;

  internal static readonly Func<object, bool, bool> RunAroundHook = CaptureRunAround;

  internal static readonly Func<object, bool> WindowOpenHook = target => target is Car locomotive && OrdersWindow.IsOpenFor(locomotive);

  internal static void Reset() => Dispatching = false;

  private static bool? GotoSmartChoice()
  {
    return (AppDomain.CurrentDomain.GetData(GotoDeadEndChoiceKey) as string) switch
    {
      GotoSmartEntrance => true,
      GotoNormalEntrance => false,
      _ => null
    };
  }

  private static bool CaptureTurntable(object target, string turntableId, int exit)
  {
    try
    {
      if (target is not Car locomotive || string.IsNullOrEmpty(turntableId) || !OrdersWindow.IsOpenFor(locomotive)) return false;
      var orders = OrderList.Read(locomotive);
      orders.Add(OrderStep.Turntable(turntableId, exit < 0 ? null : exit));
      orders.Write(locomotive);
      Toast.Present($"{locomotive.DisplayName}: step {orders.Steps.Count} added to its orders", ToastPosition.Bottom);
      return true;
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't add the turntable move to the orders");
      return false;
    }
  }

  private static bool CaptureRunAround(object target, bool front)
  {
    try
    {
      if (target is not Car locomotive || !OrdersWindow.IsOpenFor(locomotive)) return false;
      var orders = OrderList.Read(locomotive);
      orders.Add(OrderStep.RunAround(front ? CutSide.Front : CutSide.Rear));
      orders.Write(locomotive);
      Toast.Present($"{locomotive.DisplayName}: step {orders.Steps.Count} added to its orders", ToastPosition.Bottom);
      return true;
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't add the run around to the orders");
      return false;
    }
  }

  private static bool TryParseDeliver(string value, out int count, out CutSide side)
  {
    count = 0;
    side = CutSide.Auto;
    if (string.IsNullOrEmpty(value)) return false;
    var parts = value.Split(',');
    if (parts.Length != 2 || !int.TryParse(parts[0], out count) || count < 1) return false;
    side = parts[1] == "front" ? CutSide.Front : CutSide.Rear;
    return true;
  }

  internal static bool Allow(AutoEngineerOrdersHelper helper, OrderWaypoint waypoint)
  {
    try
    {
      if (Dispatching || LocomotiveField?.GetValue(helper) is not Car locomotive || !OrdersWindow.IsOpenFor(locomotive)) return true;
      var orders = OrderList.Read(locomotive);
      var station = waypoint.WantsCouple ? null : AppDomain.CurrentDomain.GetData(GotoStationKey) as string;
      var refuel = waypoint.WantsCouple ? null : AppDomain.CurrentDomain.GetData(GotoRefuelKey) as string;
      if (!string.IsNullOrEmpty(refuel))
      {
        AppDomain.CurrentDomain.SetData(GotoRefuelKey, null);
        orders.Add(OrderStep.Refuel(refuel.Split(','), OrderStep.DefaultBelow));
      }
      else if (!string.IsNullOrEmpty(station) && Passengers.Stop(station) != null)
      {
        AppDomain.CurrentDomain.SetData(GotoStationKey, null);
        orders.Add(OrderStep.ToStation(station));
      }
      else
      {
        orders.Add(OrderStep.Move(locomotive, waypoint, waypoint.WantsCouple ? null : GotoSmartChoice()));
      }
      var added = 1;
      var deliver = waypoint.WantsCouple ? null : AppDomain.CurrentDomain.GetData(GotoDeliverKey) as string;
      if (TryParseDeliver(deliver, out var count, out var side))
      {
        AppDomain.CurrentDomain.SetData(GotoDeliverKey, null);
        orders.Add(OrderStep.Cut(count, side));
        added = 2;
      }
      orders.Write(locomotive);
      AppDomain.CurrentDomain.SetData(CapturedKey, true);
      var steps = added == 1 ? $"step {orders.Steps.Count}" : $"steps {orders.Steps.Count - 1} and {orders.Steps.Count}";
      Toast.Present($"{locomotive.DisplayName}: {steps} added to its orders", ToastPosition.Bottom);
      return false;
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't add the waypoint to the orders");
      return true;
    }
  }
}

[HarmonyPatch(typeof(AutoEngineerOrdersHelper), nameof(AutoEngineerOrdersHelper.SetWaypoint))]
internal static class SetWaypointPatch
{
  private static bool Prefix(ref AutoEngineerOrdersHelper __instance, OrderWaypoint waypoint) => OrderCapture.Allow(__instance, waypoint);
}

[HarmonyPatch(typeof(AutoEngineerOrdersHelper), nameof(AutoEngineerOrdersHelper.SetWaypointOrder))]
internal static class SetWaypointOrderPatch
{
  private static bool Prefix(ref AutoEngineerOrdersHelper __instance, OrderWaypoint waypoint) => OrderCapture.Allow(__instance, waypoint);
}
