using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using Map.Runtime;
using Model;
using Model.AI;
using Track;
using UI;
using UI.Common;
using UI.EngineControls;
using UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QbanRambo.MapPlus;

internal static class MapWaypoints
{
  private const float PickRadiusFactor = 0.05f;
  private const float PlaceRadius = 3f;

  private const string MenuOwner = "Map Plus Track";

  private static readonly Color SentColor = new(0.55f, 0.9f, 1f);
  private static readonly Color RefusedColor = new(1f, 0.3f, 0.25f);
  private static readonly Color FuseeColor = new(1f, 0.45f, 0.15f);

  private static PopupMenu _menu;

  internal static void ShowMenu(MapDrag drag, Vector2 screenPoint)
  {
    try
    {
      var camera = MapBuilder.Shared?.mapCamera;
      if (camera == null || !MapMouse.TryGetRay(drag, out var ray, out _) || !MapMouse.TryGetTrack(ray, camera.orthographicSize * PickRadiusFactor, out var location)) return;
      var canvas = drag.GetComponentInParent<Canvas>();
      var root = canvas != null ? canvas.rootCanvas : null;
      if (root == null) return;
      var position = Graph.Shared.GetPosition(location);
      var items = new List<MenuItem>();
      var locomotive = TrainController.Shared?.SelectedLocomotive;
      items.Add(locomotive != null
        ? MenuItem.Action($"Send {locomotive.DisplayName} here", () => Safe(() => Send(locomotive, location)))
        : MenuItem.Action("Send a train here", null, "Select a locomotive first", true));
      if (!Fusees.CanEdit)
      {
        items.Add(MenuItem.Action("Place fusee", null, "Not allowed for your role", true));
      }
      else if (Fusees.TryNear(location, out var key))
      {
        items.Add(MenuItem.Action("Extinguish fusee", () => Safe(() =>
        {
          Fusees.Remove(key);
          MapPulse.Show(position, FuseeColor);
        })));
      }
      else
      {
        items.Add(MenuItem.Action("Place fusee", () => Safe(() =>
        {
          Fusees.Place(location);
          MapPulse.Show(position, FuseeColor);
        }), "The engineer stops here"));
      }
      _menu?.Dismiss();
      _menu = PopupMenu.PresentAt(root, screenPoint, items, MenuOwner);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't open the track menu on the map");
    }
  }

  private static void Safe(Action action)
  {
    try
    {
      action();
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "The track menu action failed");
      Toast.Present("That didn't work. See the Message Log.", ToastPosition.Bottom);
    }
  }

  private static void Send(BaseLocomotive locomotive, Location location)
  {
    try
    {
      var position = Graph.Shared.GetPosition(location);
      if (!CanReach(locomotive, location, out var failure))
      {
        MapPulse.Show(position, RefusedColor);
        Toast.Present(failure ?? $"{locomotive.DisplayName} can't find a route there.", ToastPosition.Bottom);
        return;
      }
      var orders = new AutoEngineerOrdersHelper(locomotive, new AutoEngineerPersistence(locomotive.KeyValueObject));
      orders.SetWaypointOrder(new OrderWaypoint(Graph.LocationToString(location), null));
      MapPulse.Show(position, SentColor);
      var released = ReleaseHandbrakes(locomotive);
      var place = TrackPainter.PlaceAt(position, PlaceRadius);
      var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
      Toast.Present($"{locomotive.DisplayName} heading to {place?.Name ?? "the waypoint"}{handbrakes}", ToastPosition.Bottom);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't send the locomotive from the map");
      Toast.Present("Couldn't send the locomotive. See the Message Log.", ToastPosition.Bottom);
    }
  }

  private static bool CanReach(BaseLocomotive locomotive, Location location, out string failure)
  {
    failure = null;
    var cars = Consist.Of(locomotive).ToList();
    var length = AutoEngineerPlanner.CalculateTotalLength(cars);
    var forward = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders.Forward;
    var waypoint = new OrderWaypoint(Graph.LocationToString(location), null);
    foreach (var direction in new[] { forward, !forward })
    {
      if (AutoEngineerPlanner.TryPlanRoute(locomotive, cars, length, direction, waypoint, Graph.Shared, out _, out _, out var message, out _)) return true;
      failure ??= message;
    }
    return false;
  }

  private static int ReleaseHandbrakes(BaseLocomotive locomotive)
  {
    var released = 0;
    foreach (var car in Consist.Of(locomotive))
    {
      if (car.air == null || !car.air.handbrakeApplied) continue;
      car.SetHandbrake(false);
      released++;
    }
    return released;
  }
}

[HarmonyPatch(typeof(MapDrag), nameof(MapDrag.OnPointerClick))]
internal static class MapRightClickPatch
{
  private static bool Prefix(MapDrag __instance, PointerEventData data, bool ____isDragging)
  {
    try
    {
      if (data == null || data.button != PointerEventData.InputButton.Right) return true;
      var picker = TrackLocationPicker.Shared;
      if (picker != null && picker.IsPicking) return true;
      if (!____isDragging) MapWaypoints.ShowMenu(__instance, data.position);
      return false;
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't handle a right-click on the map");
      return true;
    }
  }
}
