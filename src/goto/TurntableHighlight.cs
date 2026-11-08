using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Track;
using UI;
using UnityEngine;

namespace QbanRambo.Goto;

internal sealed class TurntableHighlight : MonoBehaviour
{
  private static readonly Dictionary<Action, IReadOnlyList<TrackSegment>> Targets = new();
  private static TurntableHighlight _shared;
  private static bool _warned;

  private PopupMenu _menu;
  private Action _shown;
  private string _token;

  internal static Action Register(Action action, IReadOnlyList<TrackSegment> segments)
  {
    if (action != null && segments != null && segments.Count > 0) Targets[action] = segments;
    return action;
  }

  internal static void Hovered(PopupMenu menu, MenuItem item)
  {
    try
    {
      if (item.OnChosen != null && Targets.TryGetValue(item.OnChosen, out var segments))
      {
        if (_shared == null) _shared = new GameObject("Goto Turntable Highlight").AddComponent<TurntableHighlight>();
        _shared.Show(menu, item.OnChosen, segments);
      }
      else if (_shared != null)
      {
        _shared.Hide();
      }
    }
    catch (Exception exception)
    {
      if (_warned) return;
      _warned = true;
      GotoMenu.Log?.Warning($"Couldn't highlight the turntable track: {exception.Message}");
    }
  }

  internal static void Clear()
  {
    Targets.Clear();
    if (_shared != null) Destroy(_shared.gameObject);
    _shared = null;
  }

  private void Show(PopupMenu menu, Action action, IReadOnlyList<TrackSegment> segments)
  {
    _menu = menu;
    if (_shown == action && _token != null) return;
    Hide();
    var indicators = SegmentIndicatorController.Shared;
    if (indicators == null) return;
    _token = indicators.Add(segments);
    _shown = action;
  }

  private void Hide()
  {
    if (_token != null && SegmentIndicatorController.Shared != null) SegmentIndicatorController.Shared.Remove(_token);
    _token = null;
    _shown = null;
  }

  private void Update()
  {
    if (_menu != null && _menu.IsOpen) return;
    Hide();
    Targets.Clear();
    Destroy(gameObject);
  }

  private void OnDestroy()
  {
    Hide();
    if (_shared == this) _shared = null;
  }
}

[HarmonyPatch(typeof(PopupMenu), "OnRowHovered")]
internal static class PopupMenuHoverPatch
{
  private static readonly System.Reflection.FieldInfo LevelsField = AccessTools.Field(typeof(PopupMenu), "_levels");
  private static System.Reflection.FieldInfo _items;
  private static bool _warned;

  private static void Postfix(PopupMenu __instance, int level, int index)
  {
    try
    {
      if (LevelsField?.GetValue(__instance) is not IList levels) return;
      if (level < 0 || level >= levels.Count) return;
      var entry = levels[level];
      _items ??= AccessTools.Field(entry.GetType(), "Items");
      if (_items?.GetValue(entry) is not IReadOnlyList<MenuItem> items || index < 0 || index >= items.Count) return;
      TurntableHighlight.Hovered(__instance, items[index]);
    }
    catch (Exception exception)
    {
      if (_warned) return;
      _warned = true;
      GotoMenu.Log?.Warning($"Couldn't follow the menu hover: {exception.Message}");
    }
  }
}
