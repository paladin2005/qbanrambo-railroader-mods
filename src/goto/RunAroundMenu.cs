using System;
using System.Collections.Generic;
using Model;
using UI;
using UI.Common;

namespace QbanRambo.Goto;

internal static class RunAroundMenu
{
  internal static MenuItem Item(BaseLocomotive locomotive)
  {
    var front = RunAroundWatch.Group(locomotive, true).Count;
    var behind = RunAroundWatch.Group(locomotive, false).Count;
    if (front == 0 && behind == 0) return MenuItem.Submenu("Run around", Array.Empty<MenuItem>(), "No cars to go around", disabled: true);
    return MenuItem.Submenu("Run around", () => GotoMenu.Safe(() => SideItems(locomotive, front, behind)));
  }

  private static IReadOnlyList<MenuItem> SideItems(BaseLocomotive locomotive, int front, int behind)
  {
    var queued = Delivery.Captures(locomotive);
    var items = new List<MenuItem>();
    if (front > 0) items.Add(SideItem(locomotive, true, front, queued));
    if (behind > 0) items.Add(SideItem(locomotive, false, behind, queued));
    return items;
  }

  private static MenuItem SideItem(BaseLocomotive locomotive, bool front, int count, bool queued)
  {
    var label = front ? "The cars in front" : "The cars behind";
    if (queued) return MenuItem.Action(label, () => Send(locomotive, front), $"Add a run around of {Delivery.Cars(count)} to its orders");
    var refusal = RunAroundWatch.Refusal(locomotive, front);
    return MenuItem.Action(label, () => Send(locomotive, front), refusal ?? $"Leave {Delivery.Cars(count)}, go around them and couple to their far end", refusal != null);
  }

  private static void Send(BaseLocomotive locomotive, bool front)
  {
    try
    {
      RunAroundWatch.Begin(locomotive, front);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Error(exception, $"Run around for {locomotive.DisplayName} failed");
      Toast.Present("The run around failed. See the Message Log.", ToastPosition.Bottom);
    }
  }
}
