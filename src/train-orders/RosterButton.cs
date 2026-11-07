using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model;
using UI.Common;
using UI.EngineRoster;
using UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.TrainOrders;

internal sealed class RosterButton : MonoBehaviour
{
  private const string TemplateAction = "ActionInspect";
  private const float FallbackGap = 4f;

  private static readonly FieldInfo EngineField = AccessTools.Field(typeof(EngineRosterRow), "_engine");
  private static bool _warned;
  private static bool _placed;

  private EngineRosterRow _row;

  internal static void Ensure(EngineRosterRow row)
  {
    if (row == null || row.GetComponent<RosterButton>() != null) return;
    var buttons = row.GetComponentsInChildren<Button>(true);
    var template = FindButton(buttons, TemplateAction) ?? buttons.OrderBy(candidate => candidate.transform.position.x).FirstOrDefault();
    if (template == null)
    {
      if (!_warned) TrainOrdersMod.Log?.Warning("Engine Roster buttons not found; no Train Orders button in the roster");
      _warned = true;
      return;
    }
    var marker = row.gameObject.AddComponent<RosterButton>();
    marker._row = row;
    var parent = template.transform.parent;
    var clone = Instantiate(template.gameObject, parent);
    clone.name = "Train Orders Button";
    var laidOut = parent.GetComponent<LayoutGroup>() != null;
    if (laidOut)
    {
      clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
    }
    else
    {
      var templateX = template.transform.localPosition.x;
      var gaps = buttons
        .Where(other => other != template && other.transform.parent == parent)
        .Select(other => Mathf.Abs(other.transform.localPosition.x - templateX))
        .Where(gap => gap > 1f)
        .ToList();
      var step = gaps.Count > 0 ? gaps.Min() : ((RectTransform)template.transform).rect.width + FallbackGap;
      clone.transform.localPosition = template.transform.localPosition - new Vector3(step, 0f, 0f);
    }
    if (!_placed)
    {
      _placed = true;
      TrainOrdersMod.Log?.Info($"Train Orders button added to the Engine Roster ({(laidOut ? "layout group" : "placed by hand")}, template {template.name})");
    }
    var button = clone.GetComponent<Button>();
    button.onClick = new Button.ButtonClickedEvent();
    button.onClick.AddListener(marker.Open);
    var glyph = GlyphImage(clone);
    if (glyph != null)
    {
      glyph.sprite = Icons.List;
      glyph.color = Color.white;
      glyph.preserveAspect = true;
    }
    var tooltip = clone.GetComponent<UITooltipProvider>();
    if (tooltip != null) tooltip.TooltipInfo = new TooltipInfo("Train Orders", "Open this locomotive's orders");
  }

  private void Open()
  {
    OrderActions.Run(() =>
    {
      if (EngineField?.GetValue(_row) is BaseLocomotive locomotive && locomotive != null) OrdersWindow.Present(locomotive);
    });
  }

  private static Button FindButton(Button[] buttons, string action)
  {
    foreach (var button in buttons)
    {
      for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
      {
        if (button.onClick.GetPersistentMethodName(i) == action) return button;
      }
    }
    return null;
  }

  private static Image GlyphImage(GameObject root)
  {
    var frame = root.GetComponent<RailroaderButton>()?.Content;
    return root.GetComponentsInChildren<Image>(true)
      .Where(image => image.gameObject != root && image != frame)
      .OrderBy(image => image.rectTransform.rect.width * image.rectTransform.rect.height)
      .FirstOrDefault();
  }

}

[HarmonyPatch(typeof(EngineRosterRow), nameof(EngineRosterRow.Configure), new[] { typeof(BaseLocomotive), typeof(bool), typeof(bool), typeof(EngineRosterPanel) })]
internal static class EngineRosterRowPatch
{
  private static bool _logged;

  private static void Postfix(EngineRosterRow __instance)
  {
    try
    {
      RosterButton.Ensure(__instance);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      TrainOrdersMod.Log?.Error(exception, "Couldn't add the Train Orders button to the Engine Roster");
    }
  }
}
