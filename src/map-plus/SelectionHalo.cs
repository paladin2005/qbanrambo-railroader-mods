using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class SelectionHalo : MonoBehaviour
{
  private const float RefreshSeconds = 0.25f;
  private const float Growth = 1.3f;
  private const string OutlineName = "Map Plus Selected";
  private const string BadgeName = "Map Plus Alert";

  private static readonly Color OutlineColor = new(0.3f, 0.85f, 1f, 1f);
  private static readonly Color ComingColor = new(1f, 0.72f, 0.15f, 1f);
  private static readonly Color SpottedColor = new(0.48f, 0.9f, 0.48f, 1f);

  private readonly Dictionary<Car, Image> _outlines = new();
  private float _nextRefresh;
  private bool _logged;

  private void OnDestroy()
  {
    foreach (var outline in _outlines.Values)
    {
      if (outline != null) Destroy(outline.gameObject);
    }
    _outlines.Clear();
  }

  private void Update()
  {
    if (Time.unscaledTime < _nextRefresh) return;
    _nextRefresh = Time.unscaledTime + RefreshSeconds;
    try
    {
      var selected = TrainController.Shared?.SelectedCar;
      var wanted = new Dictionary<Car, Color>();
      if (selected != null && !selected.IsInBardo)
      {
        foreach (var car in Consist.Of(selected).Where(car => car != null)) wanted[car] = OutlineColor;
      }
      if (PlaceIcons.Hovered is { } place)
      {
        foreach (var car in place.ComingCars.Where(car => car != null && !car.IsInBardo)) wanted[car] = ComingColor;
        foreach (var car in place.SpottedCars.Where(car => car != null && !car.IsInBardo)) wanted[car] = SpottedColor;
      }
      foreach (var car in _outlines.Keys.ToList())
      {
        var outline = _outlines[car];
        if (car != null && wanted.ContainsKey(car) && outline != null) continue;
        if (outline != null) Destroy(outline.gameObject);
        _outlines.Remove(car);
      }
      foreach (var entry in wanted)
      {
        if (!_outlines.TryGetValue(entry.Key, out var outline))
        {
          outline = CreateOutline(entry.Key);
          if (outline == null) continue;
          _outlines[entry.Key] = outline;
        }
        if (outline.color != entry.Value) outline.color = entry.Value;
      }
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't outline the selected train");
    }
  }

  private static Image CreateOutline(Car car)
  {
    if (CarIcons.MapIconField?.GetValue(car) is not MapIcon icon || icon == null) return null;
    var image = icon.GetComponentsInChildren<Image>(true).FirstOrDefault(candidate => candidate.gameObject.name != BadgeName && candidate.gameObject.name != OutlineName);
    if (image == null || image.transform.parent == null || image.gameObject == icon.gameObject) return null;
    var source = image.rectTransform;
    var outlineObject = new GameObject(OutlineName, typeof(RectTransform)) { layer = image.gameObject.layer };
    var rect = (RectTransform)outlineObject.transform;
    rect.SetParent(source.parent, false);
    rect.anchorMin = source.anchorMin;
    rect.anchorMax = source.anchorMax;
    rect.pivot = source.pivot;
    rect.sizeDelta = source.sizeDelta;
    rect.localPosition = source.localPosition;
    rect.localRotation = source.localRotation;
    rect.localScale = source.localScale * Growth;
    rect.SetSiblingIndex(source.GetSiblingIndex());
    var outline = outlineObject.AddComponent<Image>();
    outline.sprite = image.sprite;
    outline.type = image.type;
    outline.preserveAspect = image.preserveAspect;
    outline.color = OutlineColor;
    outline.raycastTarget = false;
    return outline;
  }
}
