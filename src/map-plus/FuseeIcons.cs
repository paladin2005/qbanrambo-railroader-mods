using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class FuseeIcons : MonoBehaviour
{
  private const float ListSeconds = 0.5f;
  private const float Height = 85f;
  private const float SizeFraction = 0.09f;
  private const float PulseSeconds = 1.2f;
  private const float GlowGrowth = 1.6f;
  private const int SpritePixels = 64;
  private const int Samples = 4;

  private static readonly Color32 Disc = new(28, 24, 22, 235);
  private static readonly Color32 Rim = new(240, 236, 228, 255);
  private static readonly Color32 Flame = new(236, 74, 34, 255);
  private static readonly Color32 Core = new(255, 214, 92, 255);
  private static readonly Color GlowColor = new(1f, 0.38f, 0.12f, 1f);

  private static readonly List<Marker> Markers = new();
  private static Sprite _icon;
  private static Sprite _glow;

  private GameObject _root;
  private float _nextList;
  private float _lastSize = -1f;
  private bool _logged;

  internal static Fusee? At(Vector3 gamePoint, float radius)
  {
    Marker closest = null;
    var closestDistance = radius;
    foreach (var marker in Markers)
    {
      var distance = Vector2.Distance(new Vector2(marker.Fusee.Position.x, marker.Fusee.Position.z), new Vector2(gamePoint.x, gamePoint.z));
      if (distance >= closestDistance) continue;
      closestDistance = distance;
      closest = marker;
    }
    return closest?.Fusee;
  }

  private void OnDestroy()
  {
    Markers.Clear();
    if (_root != null) Destroy(_root);
  }

  private void Update()
  {
    try
    {
      var mapShown = MapMouse.IsMapShown(out var camera);
      var shown = MapPlusMod.Fusees.IsOn && mapShown;
      if (_root != null && _root.activeSelf != shown) _root.SetActive(shown);
      if (!shown) return;
      if (Time.unscaledTime >= _nextList)
      {
        _nextList = Time.unscaledTime + ListSeconds;
        Refresh();
      }
      if (_root == null || Markers.Count == 0) return;
      _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(Vector3.zero), Quaternion.Euler(90f, 0f, 0f));
      var size = camera.orthographicSize * SizeFraction;
      var phase = Time.unscaledTime / PulseSeconds % 1f;
      var beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime / PulseSeconds * Mathf.PI * 2f);
      foreach (var marker in Markers)
      {
        if (!Mathf.Approximately(size, _lastSize))
        {
          marker.Icon.rectTransform.sizeDelta = new Vector2(size, size);
          marker.Glow.rectTransform.sizeDelta = new Vector2(size, size);
        }
        marker.Icon.rectTransform.localScale = Vector3.one * (1f + 0.12f * beat);
        marker.Glow.rectTransform.localScale = Vector3.one * (1f + GlowGrowth * phase);
        marker.Glow.color = new Color(GlowColor.r, GlowColor.g, GlowColor.b, 0.7f * (1f - phase));
      }
      _lastSize = size;
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the fusees");
    }
  }

  private void Refresh()
  {
    var fusees = Fusees.All();
    if (fusees.Count == Markers.Count && fusees.All(fusee => Markers.Any(marker => marker.Fusee.Key == fusee.Key))) return;
    Build();
    foreach (var marker in Markers)
    {
      Destroy(marker.Icon.gameObject);
      Destroy(marker.Glow.gameObject);
    }
    Markers.Clear();
    foreach (var fusee in fusees)
    {
      var glow = Image("Fusee Glow", Glow);
      var icon = Image("Fusee", Icon);
      var local = new Vector3(fusee.Position.x, fusee.Position.z, -(fusee.Position.y + Height));
      glow.rectTransform.localPosition = local;
      icon.rectTransform.localPosition = local;
      Markers.Add(new Marker(fusee, icon, glow));
    }
    _lastSize = -1f;
  }

  private Image Image(string name, Sprite sprite)
  {
    var child = new GameObject(name, typeof(RectTransform)) { layer = Layers.Map };
    child.transform.SetParent(_root.transform, false);
    var image = child.AddComponent<Image>();
    image.sprite = sprite;
    image.raycastTarget = false;
    return image;
  }

  private void Build()
  {
    if (_root != null) return;
    _root = new GameObject("Map Plus Fusees", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    if (prefabCanvas != null)
    {
      canvas.sortingLayerID = prefabCanvas.sortingLayerID;
      canvas.sortingOrder = prefabCanvas.sortingOrder + 1;
    }
  }

  private static Sprite Icon => _icon != null ? _icon : _icon = Draw(IconPixel);

  private static Sprite Glow => _glow != null ? _glow : _glow = Draw(GlowPixel);

  private static Sprite Draw(Func<float, float, Color> shade)
  {
    var pixels = new Color32[SpritePixels * SpritePixels];
    for (var y = 0; y < SpritePixels; y++)
    {
      for (var x = 0; x < SpritePixels; x++)
      {
        var sum = Color.clear;
        for (var sy = 0; sy < Samples; sy++)
        {
          for (var sx = 0; sx < Samples; sx++)
          {
            var c = shade(x + (sx + 0.5f) / Samples, y + (sy + 0.5f) / Samples);
            sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
          }
        }
        sum /= Samples * Samples;
        pixels[y * SpritePixels + x] = sum.a > 0f ? (Color32)new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : new Color32(0, 0, 0, 0);
      }
    }
    return Sprites.Create(SpritePixels, pixels);
  }

  private static Color GlowPixel(float x, float y)
  {
    var d = Vector2.Distance(new Vector2(x, y), new Vector2(32f, 32f)) / 30f;
    if (d >= 1f) return Color.clear;
    var ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.72f) / 0.28f);
    return new Color(1f, 1f, 1f, ring * ring);
  }

  private static Color IconPixel(float x, float y)
  {
    var center = new Vector2(32f, 32f);
    var d = Vector2.Distance(new Vector2(x, y), center);
    if (d > 30f) return Color.clear;
    if (d > 26.5f) return Rim;
    if (InFlame(x, y, 1f)) return InFlame(x, y, 0.55f) ? Core : Flame;
    return Disc;
  }

  private static bool InFlame(float x, float y, float scale)
  {
    var bx = 32f;
    var by = 24f;
    var radius = 11f * scale;
    var tip = by + 30f * scale;
    var dx = x - bx;
    var dy = y - by;
    if (dx * dx + dy * dy <= radius * radius) return true;
    if (y < by || y > tip) return false;
    var t = (y - by) / (tip - by);
    var half = radius * (1f - t) * (1f - 0.25f * t);
    return Mathf.Abs(dx + 3f * scale * Mathf.Sin(t * Mathf.PI)) <= half;
  }

  private sealed class Marker
  {
    public Marker(Fusee fusee, Image icon, Image glow)
    {
      Fusee = fusee;
      Icon = icon;
      Glow = glow;
    }

    public Fusee Fusee { get; }
    public Image Icon { get; }
    public Image Glow { get; }
  }
}
