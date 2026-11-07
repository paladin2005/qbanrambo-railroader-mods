using System;
using System.Collections.Generic;
using Game;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Track;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal readonly struct Fusee
{
  public Fusee(string key, Vector3 position)
  {
    Key = key;
    Position = position;
  }

  public string Key { get; }
  public Vector3 Position { get; }
}

internal static class Fusees
{
  private const float NearRadius = 20f;

  private static bool _logged;

  internal static bool CanEdit
  {
    get
    {
      try
      {
        return StateManager.CheckAuthorizedToSendMessage(new FlareRemove(null));
      }
      catch (Exception)
      {
        return false;
      }
    }
  }

  internal static List<Fusee> All()
  {
    var found = new List<Fusee>();
    try
    {
      var manager = FlareManager.Shared;
      var graph = Graph.Shared;
      if (manager == null || graph == null) return found;
      var store = manager.GetComponent<KeyValueObject>();
      if (store == null) return found;
      foreach (var entry in store.Dictionary)
      {
        var value = entry.Value;
        if (value.IsNull) continue;
        try
        {
          var location = graph.LocationFrom(value["loc"]);
          if (!location.IsValid) continue;
          found.Add(new Fusee(entry.Key, graph.GetPosition(location)));
        }
        catch (Exception)
        {
        }
      }
    }
    catch (Exception exception)
    {
      if (!_logged) MapPlusMod.Log?.Warning($"Couldn't list the fusees: {exception.Message}");
      _logged = true;
    }
    return found;
  }

  internal static bool TryNear(Location location, out string key)
  {
    key = null;
    try
    {
      return FlareManager.TryGetNearbyFlare(location, out key);
    }
    catch (Exception)
    {
      return false;
    }
  }

  internal static string Nearest(Vector3 point)
  {
    string best = null;
    var bestDistance = NearRadius;
    foreach (var fusee in All())
    {
      var distance = Vector2.Distance(new Vector2(fusee.Position.x, fusee.Position.z), new Vector2(point.x, point.z));
      if (distance >= bestDistance) continue;
      bestDistance = distance;
      best = fusee.Key;
    }
    return best;
  }

  internal static void Place(Location location)
  {
    var manager = FlareManager.Shared;
    if (manager == null) throw new InvalidOperationException("No fusee manager");
    manager.PlaceFlare(location);
  }

  internal static void Remove(string key)
  {
    StateManager.ApplyLocal(new FlareRemove(key));
  }

  internal static void Reset() => _logged = false;
}
