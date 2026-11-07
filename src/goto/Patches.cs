using System;
using System.Collections.Generic;
using HarmonyLib;
using Model;
using RollingStock;
using UI;
using UnityEngine;

namespace QbanRambo.Goto;

[HarmonyPatch(typeof(CarPickable), "HandleShowContextMenu")]
internal static class CarContextMenuPatch
{
  private static void Prefix(Car car) => GotoMenu.PendingCar = car;

  private static Exception Finalizer(Exception __exception)
  {
    GotoMenu.PendingCar = null;
    return __exception;
  }
}

[HarmonyPatch(typeof(PopupMenu), nameof(PopupMenu.PresentAt), new[] { typeof(Canvas), typeof(Vector2), typeof(IReadOnlyList<MenuItem>), typeof(string), typeof(Action) })]
internal static class PopupMenuPatch
{
  private const string ContextMenuOwner = "Context Menu";

  private static void Prefix(ref IReadOnlyList<MenuItem> items, string owner)
  {
    if (owner != ContextMenuOwner || GotoMenu.PendingCar is not BaseLocomotive locomotive) return;
    try
    {
      items = GotoMenu.WithGotoItem(items, locomotive);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Error(exception, "Couldn't add the Go to menu");
    }
  }
}
