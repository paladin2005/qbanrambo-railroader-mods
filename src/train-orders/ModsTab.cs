using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Railroader.Modding;
using TMPro;
using UI.Builder;
using UI.PreferencesWindow;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class ModsTab
{
  private const string SlotKey = "ashenmare.preferences-mods.v1";
  private const string TabId = "ashenmare.mods";
  private const float Indent = 12f;
  private const float Gap = 8f;

  private static readonly List<Action<UIPanelBuilder>> Mine = new();
  private static IModLogger _log;
  private static bool _warned;

  internal static void Add(Harmony harmony, Action<UIPanelBuilder> draw, IModLogger log)
  {
    _log = log;
    try
    {
      var target = AccessTools.Method(typeof(PreferencesBuilder), "BuildTabs", new[] { typeof(UITabbedPanelBuilder) });
      var postfix = AccessTools.Method(typeof(ModsTab), nameof(BuildTabsPostfix));
      if (target == null || postfix == null)
      {
        log?.Warning("The game's Preferences window changed, so these settings aren't in Preferences > Mods");
        return;
      }
      var patches = Harmony.GetPatchInfo(target);
      if (patches == null || patches.Postfixes.All(patch => patch.PatchMethod != postfix)) harmony.Patch(target, postfix: new HarmonyMethod(postfix));
      var slot = Slot(true);
      lock (slot) slot.Add(draw);
      Mine.Add(draw);
    }
    catch (Exception exception)
    {
      log?.Warning($"Couldn't add the settings to Preferences > Mods: {exception.Message}");
    }
  }

  internal static void RemoveAll()
  {
    try
    {
      var slot = Slot(false);
      if (slot != null)
      {
        lock (slot)
        {
          foreach (var draw in Mine) slot.Remove(draw);
        }
      }
    }
    catch (Exception exception)
    {
      _log?.Warning($"Couldn't take the settings out of Preferences > Mods: {exception.Message}");
    }
    Mine.Clear();
  }

  internal static void Section(UIPanelBuilder builder, string title, UIState<bool> expanded, Action<UIPanelBuilder> content)
  {
    builder.AddExpandable(title, expanded, content, ExpandableStyle.SectionHeader);
  }

  internal static void Toggle(UIPanelBuilder builder, string label, Func<bool> get, Action<bool> set, string tooltip)
  {
    builder.HStack(row =>
    {
      row.Spacer(Indent);
      row.AddToggle(get, value => Safely(() => set(value)));
      row.Spacer(Gap);
      row.AddLabel(label, text =>
      {
        text.fontStyle |= FontStyles.UpperCase;
        text.alignment = TextAlignmentOptions.MidlineLeft;
      }).FlexibleWidth();
    }).Tooltip(label, tooltip);
  }

  internal static string Note(string text) => $"<style=Footnote>{text}</style>";

  private static void Safely(Action action)
  {
    try
    {
      action();
    }
    catch (Exception exception)
    {
      _log?.Error(exception, "Couldn't save the setting");
    }
  }

  private static List<Action<UIPanelBuilder>> Slot(bool create)
  {
    var domain = AppDomain.CurrentDomain;
    var slot = domain.GetData(SlotKey) as List<Action<UIPanelBuilder>>;
    if (slot == null && create)
    {
      slot = new List<Action<UIPanelBuilder>>();
      domain.SetData(SlotKey, slot);
    }
    return slot;
  }

  private static void BuildTabsPostfix(UITabbedPanelBuilder builder)
  {
    try
    {
      var slot = Slot(false);
      if (slot == null || Mine.Count == 0) return;
      lock (slot)
      {
        if (slot.Count == 0 || !Mine.Contains(slot[0])) return;
      }
      builder.AddTab("Mods", TabId, Draw);
    }
    catch (Exception exception)
    {
      Warn($"Couldn't add the Mods tab to Preferences: {exception.Message}");
    }
  }

  private static void Draw(UIPanelBuilder builder)
  {
    builder.VScrollView(scroll =>
    {
      scroll.ConfigureLayout(null, new RectOffset(0, 4, 0, 0));
      scroll.AddLabel(Note("Settings for the mods you have that offer them, saved on this computer."));
      var slot = Slot(false);
      if (slot == null) return;
      Action<UIPanelBuilder>[] draws;
      lock (slot)
      {
        draws = slot.ToArray();
      }
      foreach (var draw in draws)
      {
        try
        {
          draw(scroll);
        }
        catch (Exception exception)
        {
          Warn($"A mod's settings didn't draw in Preferences > Mods: {exception.Message}");
        }
      }
    });
  }

  private static void Warn(string message)
  {
    if (_warned) return;
    _warned = true;
    _log?.Warning(message);
  }
}
