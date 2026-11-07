using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Track;
using UI;
using UI.Common;
using UnityEngine;

namespace QbanRambo.Goto;

internal static class TurntableMenu
{
  internal static MenuItem Item(BaseLocomotive locomotive)
  {
    var sites = Turntables.Sites();
    if (sites.Count == 0) return MenuItem.Submenu("Turntable", Array.Empty<MenuItem>(), "None available", disabled: true);
    return MenuItem.Submenu("Turntable", () => GotoMenu.Safe(() => SiteItems(locomotive, sites)));
  }

  private static IReadOnlyList<MenuItem> SiteItems(BaseLocomotive locomotive, List<TurntableSite> sites)
  {
    var origin = locomotive.GetCenterPosition(Graph.Shared.Current);
    return sites
      .OrderBy(site => Away(origin, site))
      .Select(site =>
      {
        var refusal = TurntableWatch.Refusal(locomotive, site);
        return MenuItem.Submenu(site.Name, () => GotoMenu.Safe(() => TrackItems(locomotive, site)), refusal ?? RefuelMenu.Distance(Away(origin, site)));
      })
      .ToList();
  }

  private static IReadOnlyList<MenuItem> TrackItems(BaseLocomotive locomotive, TurntableSite site)
  {
    var consist = new HashSet<Car>(locomotive.EnumerateCoupled());
    var items = new List<MenuItem>
    {
      MenuItem.Action("Turn around", () => Send(locomotive, site, null), "Turn the engine and come back off the same track")
    };
    foreach (var track in site.Tracks.Where(track => track.Stall))
    {
      var occupied = Turntables.IsOccupied(track, consist);
      items.Add(MenuItem.Action(track.Label, () => Send(locomotive, site, track), occupied ? "Occupied" : "Park the engine in it, front first", disabled: occupied));
    }
    foreach (var track in site.Tracks.Where(track => !track.Stall))
    {
      items.Add(MenuItem.Action(track.Label, () => Send(locomotive, site, track), "Leave on this track, front first"));
    }
    return items;
  }

  private static void Send(BaseLocomotive locomotive, TurntableSite site, PitTrack goal)
  {
    try
    {
      TurntableWatch.Begin(locomotive, site, goal);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Error(exception, $"Turntable move at {site.Turntable.id} failed");
      Toast.Present("The turntable move failed. See the Message Log.", ToastPosition.Bottom);
    }
  }

  private static float Away(Vector3 origin, TurntableSite site) => Vector3.Distance(origin, site.Turntable.Position);
}
