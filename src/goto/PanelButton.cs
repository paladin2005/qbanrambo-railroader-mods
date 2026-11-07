using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model;
using UI;
using UI.EngineControls;
using UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.Goto;

internal sealed class PanelButton : MonoBehaviour
{
  private const string MenuOwner = "Goto";
  private const float FallbackGap = 4f;
  private const int IconSize = 64;
  private const int Samples = 4;
  private const float WholeButtonShare = 0.7f;
  private const float SizeTolerance = 0.3f;
  private const float CaptionTolerance = 8f;
  private const int SearchLevels = 4;
  private const float RingLum = 60f;
  private const float StrokeLum = 45f;
  private const float InkLum = 120f;
  private const int IconGrow = 3;
  private const int FillReach = 11;
  private const int FillSamples = 6;
  private const float PinShare = 0.8f;
  private const float StrokeRadius = 1.6f;
  private const float DotShare = 1.9f;
  private const float HaloWidth = 1.2f;
  private const float HaloStrength = 0.85f;

  private static readonly Color32 HaloInk = new(16, 16, 16, 255);
  private const float PinHeight = 56f;

  private static readonly Vector2 PinCenter = new(32f, 32f);

  private static readonly Color32 Ink = new(217, 198, 166, 255);

  private static readonly FieldInfo SetButtonField = AccessTools.Field(typeof(AutoEngineerWaypointControls), "setWaypointButton");
  private static readonly FieldInfo ClearButtonField = AccessTools.Field(typeof(AutoEngineerWaypointControls), "clearWaypointButton");
  private static readonly FieldInfo IconField = AccessTools.Field(typeof(AutoEngineerWaypointControls), "setWaypointButtonImage");
  private static readonly FieldInfo SliderField = AccessTools.Field(typeof(AutoEngineerWaypointControls), "maxSpeedSlider");
  private static readonly PropertyInfo LocomotiveProperty = AccessTools.Property(typeof(EngineControlSetBase), "Locomotive");

  private static Sprite _pin;
  private static Sprite _framedPin;
  private static PopupMenu _menu;
  private static bool _warned;
  private static bool _placed;

  private AutoEngineerWaypointControls _controls;

  internal static void EnsureAll()
  {
    foreach (var controls in FindObjectsByType<AutoEngineerWaypointControls>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Ensure(controls);
  }

  internal static void RemoveAll()
  {
    foreach (var button in FindObjectsByType<PanelButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
    {
      if (button != null) Destroy(button.gameObject);
    }
    _menu = null;
  }

  internal static void Ensure(AutoEngineerWaypointControls controls)
  {
    if (controls == null || controls.GetComponentInChildren<PanelButton>(true) != null) return;
    if (SetButtonField?.GetValue(controls) is not Button waypoint || waypoint == null)
    {
      if (!_warned) GotoMenu.Log?.Warning("The engineer's waypoint button wasn't found; no Go to button on the panel");
      _warned = true;
      return;
    }
    var clear = ClearButtonField?.GetValue(controls) as Button;
    var parent = waypoint.transform.parent;
    var waypointImage = IconField?.GetValue(controls) as Image;
    var replica = Replica(waypointImage, clear != null ? clear.image : null);
    var framedTemplate = replica == null ? FramedTemplate(controls, waypoint, clear) : null;
    var clone = Instantiate(framedTemplate != null ? framedTemplate.gameObject : waypoint.gameObject, parent);
    clone.name = "Goto Button";
    Strip(clone);
    var marker = clone.AddComponent<PanelButton>();
    marker._controls = controls;
    var rect = (RectTransform)clone.transform;
    var waypointRect = (RectTransform)waypoint.transform;
    rect.anchorMin = waypointRect.anchorMin;
    rect.anchorMax = waypointRect.anchorMax;
    rect.pivot = waypointRect.pivot;
    rect.sizeDelta = waypointRect.sizeDelta;
    rect.localScale = waypointRect.localScale;
    rect.localRotation = waypointRect.localRotation;
    var last = clear != null && clear.transform.parent == parent ? (RectTransform)clear.transform : waypointRect;
    var laidOut = parent.GetComponent<LayoutGroup>() != null;
    var room = 0f;
    if (laidOut)
    {
      clone.transform.SetSiblingIndex(last.GetSiblingIndex() + 1);
    }
    else
    {
      var step = last != waypointRect ? last.anchoredPosition.x - waypointRect.anchoredPosition.x : 0f;
      if (Mathf.Abs(step) < 1f) step = waypointRect.rect.width + FallbackGap;
      rect.anchoredPosition = last.anchoredPosition + new Vector2(step, 0f);
      room = MakeRoom(controls, rect, Mathf.Max(Mathf.Abs(step) - waypointRect.rect.width, FallbackGap));
    }
    var button = clone.GetComponent<Button>();
    button.onClick = new Button.ButtonClickedEvent();
    button.onClick.AddListener(marker.Open);
    button.interactable = true;
    if (replica != null)
    {
      var image = Counterpart(waypointImage, waypoint.transform, clone.transform);
      if (image != null) image.sprite = replica;
    }
    else if (framedTemplate != null)
    {
      var glyph = Glyph(clone);
      if (glyph != null)
      {
        glyph.sprite = Pin;
        glyph.color = Color.white;
        glyph.preserveAspect = true;
      }
    }
    else
    {
      var icon = Counterpart(waypointImage, waypoint.transform, clone.transform);
      if (icon != null)
      {
        icon.sprite = FramedPin;
        icon.type = Image.Type.Simple;
        icon.color = Color.white;
        icon.preserveAspect = true;
      }
    }
    var tooltip = clone.GetComponent<UITooltipProvider>() ?? clone.AddComponent<UITooltipProvider>();
    tooltip.DynamicTooltipInfo = null;
    tooltip.TooltipInfo = new TooltipInfo("Go to", "Waybills, towns, refuel and turntables, the same as Go to in the locomotive's menu");
    if (_placed) return;
    _placed = true;
    GotoMenu.Log?.Info($"Go to button added to the engineer's panel ({(laidOut ? "layout group" : $"placed by hand, the speed slider moved over {room:0}")}, next to {last.name}, {(replica != null ? "the waypoint button's own frame" : framedTemplate != null ? $"framed like {framedTemplate.name}" : "drawn frame")})");
  }

  private static Sprite Replica(Image waypointImage, Image clearImage)
  {
    try
    {
      if (waypointImage == null || clearImage == null || waypointImage.sprite == null || clearImage.sprite == null) return null;
      var sprite = waypointImage.sprite;
      var a = Pixels(sprite, out var width, out var height);
      var b = Pixels(clearImage.sprite, out var otherWidth, out var otherHeight);
      if (a == null || b == null || width != otherWidth || height != otherHeight) return null;
      var output = Compose(a, b, width, height);
      if (output == null) return null;
      var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
      {
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.DontSave
      };
      texture.SetPixels32(output);
      texture.Apply();
      var pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
      var made = Sprite.Create(texture, new Rect(0, 0, width, height), pivot, sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, sprite.border);
      made.hideFlags = HideFlags.DontSave;
      return made;
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Info($"Couldn't copy the waypoint button's frame, drawing one instead: {exception.Message}");
      return null;
    }
  }

  private static Color32[] Compose(Color32[] a, Color32[] b, int width, int height)
  {
    var interior = Interior(b, width, height);
    if (interior == null) return null;
    var maskA = IconMask(a, interior, width, height);
    var maskB = IconMask(b, interior, width, height);
    var output = (Color32[])a.Clone();
    var pending = new List<int>();
    int minX = width, minY = height, maxX = -1, maxY = -1;
    long r = 0, g = 0, bl = 0, inked = 0;
    for (var i = 0; i < a.Length; i++)
    {
      if (!interior[i]) continue;
      var x = i % width;
      var y = i / width;
      minX = Mathf.Min(minX, x);
      maxX = Mathf.Max(maxX, x);
      minY = Mathf.Min(minY, y);
      maxY = Mathf.Max(maxY, y);
      if (Lum(a[i]) > InkLum)
      {
        r += a[i].r;
        g += a[i].g;
        bl += a[i].b;
        inked++;
      }
      if (!maskA[i]) output[i] = a[i];
      else if (!maskB[i]) output[i] = b[i];
      else pending.Add(i);
    }
    if (maxX < minX) return null;
    foreach (var i in pending)
    {
      var x = i % width;
      var y = i / width;
      long sr = 0, sg = 0, sb = 0, sa = 0, found = 0;
      for (var radius = 1; radius <= FillReach && found < FillSamples; radius++)
      {
        for (var dy = -radius; dy <= radius; dy++)
        {
          for (var dx = -radius; dx <= radius; dx++)
          {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
            int xx = x + dx, yy = y + dy;
            if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
            var j = yy * width + xx;
            if (!interior[j]) continue;
            Color32 source;
            if (!maskA[j]) source = a[j];
            else if (!maskB[j]) source = b[j];
            else continue;
            sr += source.r;
            sg += source.g;
            sb += source.b;
            sa += source.a;
            found++;
          }
        }
      }
      if (found > 0) output[i] = new Color32((byte)(sr / found), (byte)(sg / found), (byte)(sb / found), (byte)(sa / found));
    }
    var ink = inked > 0 ? new Color32((byte)(r / inked), (byte)(g / inked), (byte)(bl / inked), 255) : Ink;
    var center = new Vector2((minX + maxX + 1) / 2f, (minY + maxY + 1) / 2f);
    var scale = (maxY - minY + 1) * PinShare / PinHeight;
    var strokes = PinStrokes(1f, 0f).Select(stroke => (
      From: center + (stroke.From - PinCenter) * scale,
      To: center + (stroke.To - PinCenter) * scale,
      Radius: stroke.From == stroke.To ? StrokeRadius * DotShare : StrokeRadius)).ToList();
    for (var i = 0; i < output.Length; i++)
    {
      var x = i % width;
      var y = i / width;
      int halo = 0, covered = 0;
      for (var sy = 0; sy < Samples; sy++)
      {
        for (var sx = 0; sx < Samples; sx++)
        {
          var point = new Vector2(x + (sx + 0.5f) / Samples, y + (sy + 0.5f) / Samples);
          var nearest = strokes.Min(stroke => Distance(point, stroke.From, stroke.To) - stroke.Radius);
          if (nearest <= HaloWidth) halo++;
          if (nearest <= 0f) covered++;
        }
      }
      if (halo == 0) continue;
      var shade = halo / (float)(Samples * Samples) * HaloStrength;
      var under = output[i];
      var darker = new Color32((byte)(HaloInk.r * shade + under.r * (1f - shade)), (byte)(HaloInk.g * shade + under.g * (1f - shade)), (byte)(HaloInk.b * shade + under.b * (1f - shade)), under.a);
      output[i] = covered == 0 ? darker : Blend(darker, ink, covered / (float)(Samples * Samples));
    }
    return output;
  }

  private static bool[] Interior(Color32[] pixels, int width, int height)
  {
    var left = new int[height];
    var right = new int[height];
    var bottom = new int[width];
    var top = new int[width];
    for (var y = 0; y < height; y++)
    {
      left[y] = -1;
      right[y] = -1;
      var first = -1;
      var last = -1;
      for (var x = 0; x < width; x++)
      {
        if (!Ring(pixels[y * width + x])) continue;
        if (first < 0) first = x;
        last = x;
      }
      if (first < 0) continue;
      var end = first;
      while (end + 1 < width && Ring(pixels[y * width + end + 1])) end++;
      var start = last;
      while (start - 1 >= 0 && Ring(pixels[y * width + start - 1])) start--;
      left[y] = end;
      right[y] = start;
    }
    for (var x = 0; x < width; x++)
    {
      bottom[x] = -1;
      top[x] = -1;
      var first = -1;
      var last = -1;
      for (var y = 0; y < height; y++)
      {
        if (!Ring(pixels[y * width + x])) continue;
        if (first < 0) first = y;
        last = y;
      }
      if (first < 0) continue;
      var end = first;
      while (end + 1 < height && Ring(pixels[(end + 1) * width + x])) end++;
      var start = last;
      while (start - 1 >= 0 && Ring(pixels[(start - 1) * width + x])) start--;
      bottom[x] = end;
      top[x] = start;
    }
    var inside = new bool[pixels.Length];
    var any = false;
    for (var y = 0; y < height; y++)
    {
      for (var x = 0; x < width; x++)
      {
        if (left[y] < 0 || bottom[x] < 0) continue;
        if (x <= left[y] + 1 || x >= right[y] - 1 || y <= bottom[x] + 1 || y >= top[x] - 1) continue;
        inside[y * width + x] = true;
        any = true;
      }
    }
    return any ? inside : null;
  }

  private static bool[] IconMask(Color32[] pixels, bool[] interior, int width, int height)
  {
    var mask = new bool[pixels.Length];
    for (var i = 0; i < pixels.Length; i++)
    {
      if (!interior[i] || Lum(pixels[i]) <= StrokeLum) continue;
      var x = i % width;
      var y = i / width;
      for (var dy = -IconGrow; dy <= IconGrow; dy++)
      {
        for (var dx = -IconGrow; dx <= IconGrow; dx++)
        {
          if (dx * dx + dy * dy > IconGrow * IconGrow + 1) continue;
          int xx = x + dx, yy = y + dy;
          if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
          mask[yy * width + xx] = true;
        }
      }
    }
    return mask;
  }

  private static bool Ring(Color32 pixel) => pixel.a > 128 && Lum(pixel) > RingLum;

  private static float Lum(Color32 pixel) => (pixel.r + pixel.g + pixel.b) / 3f;

  private static Color32 Blend(Color32 under, Color32 over, float coverage)
  {
    var alpha = coverage + under.a / 255f * (1f - coverage);
    if (alpha <= 0f) return new Color32(0, 0, 0, 0);
    float Mix(byte top, byte bottom) => (top * coverage + bottom * (under.a / 255f) * (1f - coverage)) / alpha;
    return new Color32((byte)Mix(over.r, under.r), (byte)Mix(over.g, under.g), (byte)Mix(over.b, under.b), (byte)(alpha * 255f));
  }

  private static Color32[] Pixels(Sprite sprite, out int width, out int height)
  {
    width = 0;
    height = 0;
    if (sprite.packed && (sprite.packingMode != SpritePackingMode.Rectangle || sprite.packingRotation != SpritePackingRotation.None)) return null;
    var rect = sprite.packed ? sprite.textureRect : sprite.rect;
    width = Mathf.RoundToInt(rect.width);
    height = Mathf.RoundToInt(rect.height);
    if (width <= 0 || height <= 0) return null;
    var texture = sprite.texture;
    var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
    var previous = RenderTexture.active;
    try
    {
      Graphics.Blit(texture, target);
      RenderTexture.active = target;
      var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
      read.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
      read.Apply();
      var pixels = read.GetPixels32();
      Destroy(read);
      return pixels;
    }
    finally
    {
      RenderTexture.active = previous;
      RenderTexture.ReleaseTemporary(target);
    }
  }

  private static Button FramedTemplate(AutoEngineerWaypointControls controls, Button waypoint, Button clear)
  {
    var size = ((RectTransform)waypoint.transform).rect.size;
    var seen = new HashSet<Button>();
    var root = controls.transform;
    for (var level = 0; level < SearchLevels && root != null; level++, root = root.parent)
    {
      foreach (var candidate in root.GetComponentsInChildren<Button>(true))
      {
        if (candidate == null || candidate == waypoint || candidate == clear || !seen.Add(candidate) || candidate.GetComponent<PanelButton>() != null) continue;
        var candidateSize = ((RectTransform)candidate.transform).rect.size;
        if (Mathf.Abs(candidateSize.x - size.x) > size.x * SizeTolerance || Mathf.Abs(candidateSize.y - size.y) > size.y * SizeTolerance) continue;
        if (Glyph(candidate.gameObject) != null) return candidate;
      }
    }
    return null;
  }

  private static Image Glyph(GameObject root)
  {
    var rootRect = (RectTransform)root.transform;
    var frame = root.GetComponent<UI.Common.RailroaderButton>()?.Content;
    return root.GetComponentsInChildren<Image>(true)
      .Where(image => image.gameObject != root && image != frame && Area(image.rectTransform) < Area(rootRect) * WholeButtonShare && Area(image.rectTransform) > 1f)
      .OrderBy(image => Area(image.rectTransform))
      .FirstOrDefault();
  }

  private static void Strip(GameObject clone)
  {
    foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
    {
      if (behaviour == null) continue;
      var type = behaviour.GetType();
      if ((type.Namespace ?? "").StartsWith("UnityEngine") || behaviour is UI.Common.RailroaderButton || behaviour is UITooltipProvider || behaviour is Graphic) continue;
      Destroy(behaviour);
    }
  }

  private static float MakeRoom(AutoEngineerWaypointControls controls, RectTransform button, float gap)
  {
    if (SliderField?.GetValue(controls) is not Slider slider || slider == null) return 0f;
    var sliderRect = (RectTransform)slider.transform;
    var corners = new Vector3[4];
    button.GetWorldCorners(corners);
    var buttonRight = corners[2].x;
    sliderRect.GetWorldCorners(corners);
    var sliderLeft = corners[0].x;
    var worldGap = gap * (button.parent != null ? button.parent.lossyScale.x : 1f);
    var needed = buttonRight + worldGap - sliderLeft;
    if (needed <= 0.01f) return 0f;
    var parentScale = sliderRect.parent != null && sliderRect.parent.lossyScale.x > 0f ? sliderRect.parent.lossyScale.x : 1f;
    var tolerance = CaptionTolerance * parentScale;
    var captions = controls.GetComponentsInChildren<Graphic>(true)
      .Where(graphic => graphic is not Image && graphic is not RawImage && !graphic.transform.IsChildOf(sliderRect))
      .Where(graphic =>
      {
        graphic.rectTransform.GetWorldCorners(corners);
        return Mathf.Abs(corners[0].x - sliderLeft) <= tolerance;
      })
      .ToList();
    sliderRect.offsetMin += new Vector2(needed / parentScale, 0f);
    foreach (var caption in captions) caption.rectTransform.position += new Vector3(needed, 0f, 0f);
    return needed / parentScale;
  }

  private void Open()
  {
    try
    {
      var locomotive = LocomotiveProperty?.GetValue(_controls) as BaseLocomotive;
      if (locomotive == null) locomotive = TrainController.Shared != null ? TrainController.Shared.SelectedLocomotive : null;
      if (locomotive == null) return;
      if (_menu != null) _menu.Dismiss();
      _menu = PopupMenu.PresentUnder((RectTransform)transform, GotoMenu.Safe(() => GotoMenu.GotoItems(locomotive)), MenuOwner);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Error(exception, "Couldn't open the Go to menu from the engineer's panel");
    }
  }

  private static Image Counterpart(Image image, Transform templateRoot, Transform cloneRoot)
  {
    if (image != null)
    {
      if (image.transform == templateRoot) return cloneRoot.GetComponent<Image>();
      if (image.transform.IsChildOf(templateRoot))
      {
        var names = new List<string>();
        for (var current = image.transform; current != templateRoot; current = current.parent) names.Insert(0, current.name);
        var found = cloneRoot.Find(string.Join("/", names));
        if (found != null && found.GetComponent<Image>() is { } match) return match;
      }
    }
    return cloneRoot.GetComponentsInChildren<Image>(true)
      .Where(candidate => candidate.transform != cloneRoot)
      .OrderBy(candidate => candidate.rectTransform.rect.width * candidate.rectTransform.rect.height)
      .FirstOrDefault();
  }

  private static float Area(RectTransform rect) => Mathf.Abs(rect.rect.width * rect.rect.height);

  private static Sprite Pin => _pin != null ? _pin : _pin = Draw(PinStrokes(1f, 0f), null);

  private static Sprite FramedPin => _framedPin != null ? _framedPin : _framedPin = Draw(PinStrokes(0.62f, 1.5f), Frame);

  private static List<(Vector2 From, Vector2 To, float Radius)> PinStrokes(float scale, float drop)
  {
    var center = new Vector2(32f, 32f);
    Vector2 At(float x, float y) => center + (new Vector2(x, y) - center) * scale - new Vector2(0f, drop);
    var strokes = new List<(Vector2 From, Vector2 To, float Radius)>();
    const int steps = 30;
    for (var i = 0; i < steps; i++)
    {
      var a = Mathf.Deg2Rad * Mathf.Lerp(-38f, 218f, i / (float)steps);
      var b = Mathf.Deg2Rad * Mathf.Lerp(-38f, 218f, (i + 1) / (float)steps);
      strokes.Add((At(32f + Mathf.Cos(a) * 15f, 40f + Mathf.Sin(a) * 15f), At(32f + Mathf.Cos(b) * 15f, 40f + Mathf.Sin(b) * 15f), 3f * Mathf.Max(scale, 0.8f)));
    }
    strokes.Add((At(20.2f, 30.8f), At(32f, 8f), 3f * Mathf.Max(scale, 0.8f)));
    strokes.Add((At(43.8f, 30.8f), At(32f, 8f), 3f * Mathf.Max(scale, 0.8f)));
    strokes.Add((At(32f, 40f), At(32f, 40f), 5f * Mathf.Max(scale, 0.8f)));
    return strokes;
  }

  private static float Frame(Vector2 point)
  {
    const float inset = 3f;
    const float radius = 10f;
    const float half = 32f - inset;
    var q = new Vector2(Mathf.Abs(point.x - 32f), Mathf.Abs(point.y - 32f)) - new Vector2(half - radius, half - radius);
    var outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    return Mathf.Abs(outside) - 1.6f;
  }

  private static Sprite Draw(List<(Vector2 From, Vector2 To, float Radius)> strokes, Func<Vector2, float> frame)
  {
    var pixels = new Color32[IconSize * IconSize];
    for (var y = 0; y < IconSize; y++)
    {
      for (var x = 0; x < IconSize; x++)
      {
        var covered = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
          for (var sx = 0; sx < Samples; sx++)
          {
            var point = new Vector2(x + (sx + 0.5f) / Samples, y + (sy + 0.5f) / Samples);
            var inside = frame != null && frame(point) <= 0f;
            foreach (var (from, to, radius) in strokes)
            {
              if (inside) break;
              inside = Distance(point, from, to) <= radius;
            }
            if (inside) covered++;
          }
        }
        pixels[y * IconSize + x] = new Color32(Ink.r, Ink.g, Ink.b, (byte)(covered * 255 / (Samples * Samples)));
      }
    }
    var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
    {
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSave
    };
    texture.SetPixels32(pixels);
    texture.Apply();
    var sprite = Sprite.Create(texture, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
    sprite.hideFlags = HideFlags.DontSave;
    return sprite;
  }

  private static float Distance(Vector2 point, Vector2 from, Vector2 to)
  {
    var along = to - from;
    if (along.sqrMagnitude < 0.0001f) return Vector2.Distance(point, from);
    var t = Mathf.Clamp01(Vector2.Dot(point - from, along) / along.sqrMagnitude);
    return Vector2.Distance(point, from + along * t);
  }
}

[HarmonyPatch(typeof(AutoEngineerWaypointControls), "Awake")]
internal static class WaypointControlsPatch
{
  private static bool _logged;

  private static void Postfix(AutoEngineerWaypointControls __instance)
  {
    try
    {
      PanelButton.Ensure(__instance);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      GotoMenu.Log?.Error(exception, "Couldn't add the Go to button to the engineer's panel");
    }
  }
}
