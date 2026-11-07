using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Map.Runtime.MaskComponents;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebRivers
{
  private static bool _logged;

  internal static List<float[]> Paths()
  {
    var paths = new List<float[]>();
    try
    {
      foreach (var river in UnityEngine.Object.FindObjectsByType<RiverPath>(FindObjectsInactive.Include, FindObjectsSortMode.None))
      {
        if (river == null || river.style != RiverPath.RiverPathStyle.River) continue;
        var points = river.TransformedPoints;
        if (points == null || points.Count < 2) continue;
        var path = new float[points.Count * 3];
        for (var i = 0; i < points.Count; i++)
        {
          var game = WorldTransformer.WorldToGame(points[i].position);
          path[i * 3] = Round(game.x);
          path[i * 3 + 1] = Round(game.z);
          path[i * 3 + 2] = Round(Mathf.Max(0.5f, points[i].width));
        }
        paths.Add(path);
      }
      if (!_logged)
      {
        _logged = true;
        MapPlusMod.Log?.Info($"Web map: {paths.Count} rivers");
      }
    }
    catch (Exception exception)
    {
      if (!_logged) MapPlusMod.Log?.Warning($"The web map couldn't read the rivers: {exception.Message}");
      _logged = true;
    }
    return paths;
  }

  internal static void Reset() => _logged = false;

  private static float Round(float value) => (float)Math.Round(value, 1);
}
