using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Helpers;
using Model.Ops;
using TMPro;
using Track;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class WebLabels
{
  private static bool _logged;

  internal static List<object[]> All()
  {
    var labels = new List<object[]>();
    try
    {
      var graph = Graph.Shared;
      var towns = new HashSet<string>((OpsController.Shared?.Areas ?? Enumerable.Empty<Area>()).Select(area => area.name?.Trim()).Where(name => !string.IsNullOrEmpty(name)), StringComparer.OrdinalIgnoreCase);
      var seen = new HashSet<string>();
      var names = new List<string>();
      foreach (var label in UnityEngine.Object.FindObjectsByType<MapLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
      {
        if (label == null) continue;
        var text = label.GetComponentInChildren<TMP_Text>(true);
        var value = Plain(!string.IsNullOrWhiteSpace(label.text) ? label.text : text != null ? text.text : null);
        if (string.IsNullOrEmpty(value) || towns.Contains(value)) continue;
        var anchor = Center(label, text);
        var game = WorldTransformer.WorldToGame(anchor);
        var key = $"{value}|{Mathf.RoundToInt(game.x)}|{Mathf.RoundToInt(game.z)}";
        if (!seen.Add(key)) continue;
        var right = text != null ? text.transform.right : label.transform.right;
        var flat = new Vector2(right.x, right.z);
        var angle = flat.sqrMagnitude < 0.01f ? 0f : Mathf.Atan2(flat.y, flat.x) * Mathf.Rad2Deg;
        if (angle > 90f) angle -= 180f;
        else if (angle < -90f) angle += 180f;
        var group = GroupFor(label);
        var dim = graph != null && !string.IsNullOrEmpty(group) && !graph.availableGroupIds.Contains(group);
        labels.Add(new object[] { Round(game.x), Round(game.z), value, Round(angle), dim ? 1 : 0 });
        names.Add(value);
      }
      if (!_logged && labels.Count > 0)
      {
        _logged = true;
        MapPlusMod.Log?.Info($"Web map: {labels.Count} map labels ({string.Join(", ", names.Distinct().OrderBy(name => name))})");
      }
    }
    catch (Exception exception)
    {
      if (!_logged) MapPlusMod.Log?.Warning($"The web map couldn't read the map labels: {exception.Message}");
      _logged = true;
    }
    return labels;
  }

  internal static void Reset() => _logged = false;

  private static Vector3 Center(MapLabel label, TMP_Text text)
  {
    if (text != null && text.rectTransform != null)
    {
      var rect = text.rectTransform;
      return rect.TransformPoint(rect.rect.center);
    }
    return label.transform.position;
  }

  private static string GroupFor(MapLabel label)
  {
    var parent = label.transform;
    while (parent != null)
    {
      foreach (var marker in parent.GetComponents<TrackMarker>())
      {
        var location = marker.Location;
        if (location.HasValue && location.Value.segment != null) return location.Value.segment.groupId;
      }
      parent = parent.parent;
    }
    return null;
  }

  private static string Plain(string text)
  {
    if (string.IsNullOrWhiteSpace(text)) return null;
    var plain = Regex.Replace(text, "<[^>]*>", "").Replace("\r", "");
    var lines = plain.Split('\n').Select(line => Regex.Replace(line, "\\s+", " ").Trim()).Where(line => line.Length > 0);
    plain = string.Join("\n", lines);
    return plain.Length == 0 ? null : plain;
  }

  private static float Round(float value) => (float)Math.Round(value, 1);
}
