using System;
using Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class ScaleBar : MonoBehaviour
{
  private const float MaxUnits = 110f;
  private const float Padding = 8f;
  private const float Gap = 6f;
  private const float BoxHeight = 22f;
  private const float BarHeight = 3f;
  private const float TickHeight = 9f;
  private const float TickWidth = 2f;
  private const float FontSize = 13f;
  private const float MetersToFeet = 3.28084f;
  private const float FeetPerMile = 5280f;
  private static readonly Vector2 Offset = new(10f, 44f);

  private static readonly (float Value, string Unit)[] Steps =
  {
    (10f, "ft"), (20f, "ft"), (50f, "ft"), (100f, "ft"), (200f, "ft"), (500f, "ft"), (1000f, "ft"), (2000f, "ft"),
    (0.5f, "mi"), (1f, "mi"), (2f, "mi"), (5f, "mi"), (10f, "mi"), (20f, "mi")
  };

  private RectTransform _root;
  private RectTransform _bar;
  private RectTransform _left;
  private RectTransform _right;
  private TMP_Text _label;
  private float _lastUnit = -1f;
  private bool _logged;

  private void OnDestroy()
  {
    if (_root != null) Destroy(_root.gameObject);
  }

  private void Update()
  {
    try
    {
      if (!MapMouse.IsMapShown(out var camera)) return;
      if (_root == null && !Build()) return;
      var unit = MapMouse.MetersPerUnit(camera);
      if (Mathf.Approximately(unit, _lastUnit)) return;
      _lastUnit = unit;
      Layout(unit);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the map scale");
    }
  }

  private void Layout(float metersPerUnit)
  {
    var feetPerUnit = metersPerUnit * MetersToFeet;
    var choice = Steps[0];
    foreach (var step in Steps)
    {
      if (Feet(step) / feetPerUnit <= MaxUnits) choice = step;
    }
    var width = Mathf.Round(Feet(choice) / feetPerUnit);
    var text = $"{choice.Value:0.#} {choice.Unit}";
    _label.text = text;
    var labelWidth = _label.GetPreferredValues(text).x;
    _bar.anchoredPosition = new Vector2(Padding, 0f);
    _bar.sizeDelta = new Vector2(width, BarHeight);
    _left.anchoredPosition = new Vector2(Padding, 0f);
    _right.anchoredPosition = new Vector2(Padding + width - TickWidth, 0f);
    _label.rectTransform.anchoredPosition = new Vector2(Padding + width + Gap, 0f);
    _label.rectTransform.sizeDelta = new Vector2(labelWidth + 2f, BoxHeight);
    _root.sizeDelta = new Vector2(Padding + width + Gap + labelWidth + Padding, BoxHeight);
  }

  private static float Feet((float Value, string Unit) step) => step.Unit == "mi" ? step.Value * FeetPerMile : step.Value;

  private bool Build()
  {
    var drag = MapMouse.Drag;
    if (drag == null) return false;
    var rootObject = new GameObject("Map Plus Scale", typeof(RectTransform));
    _root = (RectTransform)rootObject.transform;
    _root.SetParent(drag.transform, false);
    _root.anchorMin = _root.anchorMax = Vector2.zero;
    _root.pivot = Vector2.zero;
    _root.anchoredPosition = Offset;
    var background = rootObject.AddComponent<Image>();
    background.sprite = Glyphs.Pill;
    background.type = Image.Type.Sliced;
    background.pixelsPerUnitMultiplier = 2f;
    background.raycastTarget = false;
    _bar = Piece("Bar", BarHeight);
    _left = Piece("Left", TickHeight);
    _right = Piece("Right", TickHeight);
    _left.sizeDelta = _right.sizeDelta = new Vector2(TickWidth, TickHeight);
    var labelObject = new GameObject("Label", typeof(RectTransform));
    var labelRect = (RectTransform)labelObject.transform;
    labelRect.SetParent(_root, false);
    labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
    labelRect.pivot = new Vector2(0f, 0.5f);
    _label = labelObject.AddComponent<TextMeshProUGUI>();
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var source = prefab != null ? prefab.GetComponentInChildren<TMP_Text>(true) : null;
    if (source != null)
    {
      _label.font = source.font;
      _label.fontSharedMaterial = source.fontSharedMaterial;
    }
    _label.fontSize = FontSize;
    _label.color = new Color(0.93f, 0.91f, 0.85f);
    _label.alignment = TextAlignmentOptions.MidlineLeft;
    _label.textWrappingMode = TextWrappingModes.NoWrap;
    _label.raycastTarget = false;
    return true;
  }

  private RectTransform Piece(string name, float height)
  {
    var pieceObject = new GameObject(name, typeof(RectTransform));
    var rect = (RectTransform)pieceObject.transform;
    rect.SetParent(_root, false);
    rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
    rect.pivot = new Vector2(0f, 0.5f);
    rect.sizeDelta = new Vector2(1f, height);
    var image = pieceObject.AddComponent<Image>();
    image.color = new Color(0.93f, 0.91f, 0.85f);
    image.raycastTarget = false;
    return rect;
  }
}
