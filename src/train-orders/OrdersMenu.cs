using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model;
using RollingStock;
using UI;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class OrdersMenu
{
  internal static Car PendingCar;

  internal static IReadOnlyList<MenuItem> WithOrdersItem(IReadOnlyList<MenuItem> items, BaseLocomotive locomotive)
  {
    var list = items.ToList();
    var select = list.FindIndex(item => item.Label == "Select" || item.Label == "Deselect");
    list.Insert(select < 0 ? list.Count : select + 1, MenuItem.Action("Train Orders", () => OrderActions.Run(() => OrdersWindow.Present(locomotive))));
    return list;
  }
}

[HarmonyPatch(typeof(CarPickable), "HandleShowContextMenu")]
internal static class CarContextMenuPatch
{
  private static void Prefix(Car car) => OrdersMenu.PendingCar = car;

  private static Exception Finalizer(Exception __exception)
  {
    OrdersMenu.PendingCar = null;
    return __exception;
  }
}

[HarmonyPatch(typeof(PopupMenu), nameof(PopupMenu.PresentAt), new[] { typeof(Canvas), typeof(Vector2), typeof(IReadOnlyList<MenuItem>), typeof(string), typeof(Action) })]
internal static class PopupMenuPatch
{
  private const string ContextMenuOwner = "Context Menu";

  private static void Prefix(ref IReadOnlyList<MenuItem> items, string owner)
  {
    if (owner != ContextMenuOwner || OrdersMenu.PendingCar is not BaseLocomotive locomotive) return;
    try
    {
      items = OrdersMenu.WithOrdersItem(items, locomotive);
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't add the Train Orders menu");
    }
  }
}
