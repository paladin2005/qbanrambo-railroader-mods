using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.Zoom))]
internal static class ZoomLimitsPatch
{
  internal const float GameMinSize = 50f;
  internal const float GameMaxSize = 10000f;
  private const float MaxSize = 25000f;

  private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
  {
    var replaced = 0;
    foreach (var instruction in instructions)
    {
      if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Mathf.Approximately(value, GameMaxSize))
      {
        instruction.operand = MaxSize;
        replaced++;
      }
      yield return instruction;
    }
    if (replaced != 1) MapPlusMod.Log?.Warning($"Map zoom limit found {replaced} times instead of once, zoom stays at the game's range");
  }
}

[HarmonyPatch(typeof(MapBuilder), "UpdateForZoom")]
internal static class ZoomScalePatch
{
  private const float GameMinIconScale = 0.2f;
  private const float GameMaxIconScale = 8f;
  private const float LabelZoomFactor = 4f;

  private static void Postfix(MapBuilder __instance, ref float ____segmentLineWidth, MapTrackMeshRenderer ____meshRenderer, HashSet<MapIcon> ____mapIcons, Dictionary<MapLabel, string> ____mapLabels)
  {
    CarIcons.ZoomVersion++;
    try
    {
      var size = __instance.mapCamera.orthographicSize;
      if (size <= ZoomLimitsPatch.GameMaxSize) return;
      var factor = size / ZoomLimitsPatch.GameMaxSize;
      ____segmentLineWidth *= factor;
      ____meshRenderer?.SetLineRadius(____segmentLineWidth * 0.5f);
      var iconScale = Mathf.Lerp(GameMinIconScale, GameMaxIconScale, Mathf.InverseLerp(ZoomLimitsPatch.GameMinSize, ZoomLimitsPatch.GameMaxSize, size)) * factor;
      if (____mapIcons != null)
      {
        foreach (var icon in ____mapIcons)
        {
          if (icon != null) icon.SetZoom(iconScale);
        }
      }
      if (____mapLabels == null) return;
      foreach (var label in ____mapLabels.Keys)
      {
        if (label != null) label.SetZoom(iconScale * LabelZoomFactor);
      }
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't scale the map for the extended zoom");
    }
  }
}
