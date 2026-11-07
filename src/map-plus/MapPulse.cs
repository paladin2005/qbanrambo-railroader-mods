using Helpers;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class MapPulse : MonoBehaviour
{
  private const float Height = 100f;
  private const float Duration = 1.6f;
  private const float RingDuration = 1f;
  private const float RingDelay = 0.4f;
  private const float DotFade = 0.4f;
  private const float ViewFraction = 0.2f;
  private const float DotSize = 0.25f;
  private const int SpriteSize = 128;
  private const float RingWidth = 10f;

  private static Sprite _ringSprite;
  private static Sprite _dotSprite;

  private Vector3 _gamePosition;
  private Color _color;
  private Image[] _rings;
  private Image _dot;
  private float _start;

  internal static void Show(Vector3 gamePosition, Color color)
  {
    var builder = MapBuilder.Shared;
    if (builder == null || builder.mapCamera == null) return;
    var pulseObject = new GameObject("Map Plus Pulse", typeof(RectTransform)) { layer = Layers.Map };
    ((RectTransform)pulseObject.transform).sizeDelta = Vector2.one;
    var canvas = pulseObject.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    if (prefabCanvas != null)
    {
      canvas.sortingLayerID = prefabCanvas.sortingLayerID;
      canvas.sortingOrder = prefabCanvas.sortingOrder + CarIcons.LocomotiveOrderOffset + 1;
    }
    var pulse = pulseObject.AddComponent<MapPulse>();
    pulse._gamePosition = gamePosition;
    pulse._color = color;
    pulse._rings = new[] { pulse.AddImage(RingSprite), pulse.AddImage(RingSprite) };
    pulse._dot = pulse.AddImage(DotSprite);
    pulse._start = Time.unscaledTime;
    pulse.Animate();
  }

  private static Sprite RingSprite => _ringSprite != null ? _ringSprite : _ringSprite = CreateSprite(SpriteSize / 2f - RingWidth);

  private static Sprite DotSprite => _dotSprite != null ? _dotSprite : _dotSprite = CreateSprite(0f);

  private static Sprite CreateSprite(float innerRadius)
  {
    var pixels = new Color32[SpriteSize * SpriteSize];
    var radius = SpriteSize / 2f;
    var center = new Vector2(radius, radius);
    for (var y = 0; y < SpriteSize; y++)
    {
      for (var x = 0; x < SpriteSize; x++)
      {
        var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
        var alpha = Mathf.Clamp01(radius - distance) * (innerRadius > 0f ? Mathf.Clamp01(distance - innerRadius) : 1f);
        pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
      }
    }
    return Sprites.Create(SpriteSize, pixels);
  }

  private Image AddImage(Sprite sprite)
  {
    var child = new GameObject("Pulse", typeof(RectTransform)) { layer = gameObject.layer };
    var rect = (RectTransform)child.transform;
    rect.SetParent(transform, false);
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
    var image = child.AddComponent<Image>();
    image.sprite = sprite;
    image.raycastTarget = false;
    image.color = Color.clear;
    return image;
  }

  private void Update() => Animate();

  private void Animate()
  {
    var elapsed = Time.unscaledTime - _start;
    var builder = MapBuilder.Shared;
    if (elapsed >= Duration || builder == null || builder.mapCamera == null)
    {
      Destroy(gameObject);
      return;
    }
    transform.SetPositionAndRotation(WorldTransformer.GameToWorld(_gamePosition) + Vector3.up * Height, Quaternion.Euler(90f, 0f, 0f));
    transform.localScale = Vector3.one * (builder.mapCamera.orthographicSize * ViewFraction);
    for (var i = 0; i < _rings.Length; i++)
    {
      var progress = (elapsed - i * RingDelay) / RingDuration;
      var ring = _rings[i];
      if (progress < 0f || progress > 1f)
      {
        ring.color = Color.clear;
        continue;
      }
      var eased = 1f - (1f - progress) * (1f - progress);
      ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(DotSize, 1f, eased);
      ring.color = new Color(_color.r, _color.g, _color.b, 1f - progress);
    }
    _dot.rectTransform.localScale = Vector3.one * DotSize;
    _dot.color = new Color(_color.r, _color.g, _color.b, Mathf.Clamp01((Duration - elapsed) / DotFade));
  }
}
