using System;
using System.Collections.Generic;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class Glyphs
{
  internal const int Size = 64;
  private const float Radius = 27f;
  private const float Outline = 3f;
  private const float RingWidth = 1.8f;
  private const int Samples = 4;
  private const float Scale = 0.6f;
  private const float Stroke = 0.17f;
  private const int PillSize = 32;
  private const int PillRadius = 12;

  private static readonly Color Dark = new(0.04f, 0.035f, 0.03f, 0.92f);
  private static readonly Color RingColor = new(1f, 1f, 1f, 0.75f);
  private static readonly Color EmptyColor = new(1f, 0.25f, 0.22f, 1f);

  internal static readonly Dictionary<string, Color> KindColors = new()
  {
    { "mine", Hex(0x66717F) },
    { "logging", Hex(0x4C9A52) },
    { "mill", Hex(0xC0773A) },
    { "freight", Hex(0xA08A5E) },
    { "oil", Hex(0x7A5230) },
    { "mow", Hex(0xC49A22) },
    { "interchange", Hex(0x8A63C9) },
    { "station", Hex(0x3C86D0) },
    { "engine", Hex(0xB14B3A) },
    { "water", Hex(0x2F7FCB) },
    { "coal", Hex(0x3A3631) },
    { "diesel", Hex(0xC79A1E) }
  };

  private static readonly Dictionary<string, Sprite> Cache = new();
  private static Sprite _pill;

  internal static Sprite Place(string kind, bool empty)
  {
    var key = empty ? kind + ":empty" : kind;
    if (Cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;
    sprite = Draw(KindColors.TryGetValue(kind, out var color) ? color : KindColors["freight"], empty ? EmptyColor : RingColor, GlyphFor(kind), empty);
    Cache[key] = sprite;
    return sprite;
  }

  internal static Sprite Disc(Color fill, Color ring)
  {
    var key = $"disc:{ColorUtility.ToHtmlStringRGBA(fill)}:{ColorUtility.ToHtmlStringRGBA(ring)}";
    if (Cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;
    sprite = Draw(fill, ring, null, false);
    Cache[key] = sprite;
    return sprite;
  }

  internal static Sprite Pill => _pill != null ? _pill : _pill = CreatePill();

  private static Sprite Draw(Color fill, Color ring, Shape glyph, bool slash)
  {
    var pixels = new Color32[Size * Size];
    var center = Size / 2f;
    var slashShape = slash ? new Shape().Line(0.2f, -0.68f, 0.68f, 0.68f, -0.68f) : null;
    for (var y = 0; y < Size; y++)
    {
      for (var x = 0; x < Size; x++)
      {
        var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
        var color = Color.clear;
        color = Over(color, Dark, Mathf.Clamp01(Radius + Outline - distance + 0.5f));
        color = Over(color, fill, Mathf.Clamp01(Radius - distance + 0.5f));
        color = Over(color, ring, Mathf.Clamp01(RingWidth / 2f - Mathf.Abs(distance - (Radius - RingWidth / 2f)) + 0.5f));
        if (glyph != null) color = Over(color, Color.white, Coverage(glyph, x, y));
        if (slashShape != null) color = Over(color, EmptyColor, Coverage(slashShape, x, y));
        pixels[y * Size + x] = color;
      }
    }
    return Sprites.Create(Size, pixels);
  }

  private static float Coverage(Shape shape, int x, int y)
  {
    var hits = 0;
    for (var sy = 0; sy < Samples; sy++)
    {
      for (var sx = 0; sx < Samples; sx++)
      {
        var px = x + (sx + 0.5f) / Samples;
        var py = y + (sy + 0.5f) / Samples;
        var u = (px - Size / 2f) / Radius;
        var v = (Size / 2f - py) / Radius;
        if (shape.Contains(u, v)) hits++;
      }
    }
    return hits / (float)(Samples * Samples);
  }

  private static Color Over(Color under, Color over, float coverage)
  {
    var alpha = over.a * coverage;
    if (alpha <= 0f) return under;
    var outAlpha = alpha + under.a * (1f - alpha);
    if (outAlpha <= 0f) return Color.clear;
    var r = (over.r * alpha + under.r * under.a * (1f - alpha)) / outAlpha;
    var g = (over.g * alpha + under.g * under.a * (1f - alpha)) / outAlpha;
    var b = (over.b * alpha + under.b * under.a * (1f - alpha)) / outAlpha;
    return new Color(r, g, b, outAlpha);
  }

  private static Sprite CreatePill()
  {
    var pixels = new Color32[PillSize * PillSize];
    for (var y = 0; y < PillSize; y++)
    {
      for (var x = 0; x < PillSize; x++)
      {
        var dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - PillSize / 2f) - (PillSize / 2f - PillRadius));
        var dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - PillSize / 2f) - (PillSize / 2f - PillRadius));
        var alpha = Mathf.Clamp01(PillRadius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        pixels[y * PillSize + x] = new Color(0.086f, 0.075f, 0.055f, 0.9f * alpha);
      }
    }
    var texture = new Texture2D(PillSize, PillSize, TextureFormat.RGBA32, false)
    {
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSave
    };
    texture.SetPixels32(pixels);
    texture.Apply();
    var sprite = Sprite.Create(texture, new Rect(0, 0, PillSize, PillSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(PillRadius, PillRadius, PillRadius, PillRadius));
    sprite.hideFlags = HideFlags.DontSave;
    return sprite;
  }

  private static Color Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

  private static Shape GlyphFor(string kind)
  {
    const float s = Scale;
    var w = Stroke;
    switch (kind)
    {
      case "mine":
        return new Shape()
          .Line(w, -s * 0.75f, s * 0.85f, s * 0.45f, -s * 0.35f)
          .Line(w, Curve.Quad(-s * 0.55f, -s * 0.85f, s * 0.75f, -s * 0.85f, s * 0.9f, s * 0.45f));
      case "logging":
        return new Shape()
          .Fill(0f, -s, s * 0.8f, s * 0.35f, -s * 0.8f, s * 0.35f)
          .Line(w, 0f, s * 0.35f, 0f, s);
      case "mill":
        return new Shape().Fill(-s, s, -s, -s * 0.15f, -s * 0.35f, -s * 0.55f, -s * 0.35f, -s * 0.15f, s * 0.3f, -s * 0.55f, s * 0.3f, -s * 0.15f, s * 0.55f, -s * 0.15f, s * 0.55f, -s, s, -s, s, s);
      case "freight":
        return new Shape()
          .Line(w, -s * 0.8f, -s * 0.65f, s * 0.8f, -s * 0.65f, s * 0.8f, s * 0.75f, -s * 0.8f, s * 0.75f, -s * 0.8f, -s * 0.65f)
          .Line(w, -s * 0.8f, -s * 0.15f, s * 0.8f, -s * 0.15f)
          .Line(w, 0f, -s * 0.65f, 0f, -s * 0.15f);
      case "oil":
      case "water":
        var drop = new List<float>();
        drop.AddRange(Curve.Cubic(0f, -s, s * 0.95f, s * 0.05f, s * 0.75f, s, 0f, s));
        drop.AddRange(Curve.Cubic(0f, s, -s * 0.75f, s, -s * 0.95f, s * 0.05f, 0f, -s));
        return new Shape().Fill(drop.ToArray());
      case "mow":
        return new Shape()
          .Line(w, -s, -s * 0.35f, s, -s * 0.35f)
          .Line(w, -s, s * 0.35f, s, s * 0.35f)
          .Line(w, -s * 0.6f, -s * 0.85f, -s * 0.6f, s * 0.85f)
          .Line(w, 0f, -s * 0.85f, 0f, s * 0.85f)
          .Line(w, s * 0.6f, -s * 0.85f, s * 0.6f, s * 0.85f);
      case "interchange":
        return new Shape()
          .Line(w, -s * 0.85f, -s * 0.35f, s * 0.85f, -s * 0.35f, s * 0.45f, -s * 0.75f)
          .Line(w, s * 0.85f, s * 0.35f, -s * 0.85f, s * 0.35f, -s * 0.45f, s * 0.75f);
      case "station":
        return new Shape()
          .Circle(0f, -s * 0.55f, s * 0.32f)
          .RoundRect(-s * 0.5f, -s * 0.12f, s, s * 1.1f, s * 0.3f);
      case "engine":
        return new Shape()
          .Rect(-s * 0.95f, -s * 0.2f, s * 1.3f, s * 0.75f)
          .Rect(s * 0.35f, -s * 0.8f, s * 0.6f, s * 1.35f)
          .Rect(-s * 0.7f, -s * 0.7f, s * 0.25f, s * 0.5f)
          .Circle(-s * 0.6f, s * 0.78f, s * 0.24f)
          .Circle(-s * 0.05f, s * 0.78f, s * 0.24f)
          .Circle(s * 0.55f, s * 0.78f, s * 0.24f);
      case "coal":
        return new Shape().Fill(-s * 0.9f, s * 0.6f, -s * 0.5f, -s * 0.2f, -s * 0.1f, -s * 0.05f, s * 0.2f, -s * 0.6f, s * 0.75f, -s * 0.1f, s * 0.9f, s * 0.6f);
      case "diesel":
        return new Shape()
          .Rect(-s * 0.7f, -s * 0.85f, s * 0.95f, s * 1.75f)
          .Line(w, s * 0.25f, -s * 0.35f, s * 0.8f, -s * 0.05f, s * 0.8f, s * 0.6f);
      default:
        return new Shape().Circle(0f, 0f, s * 0.35f);
    }
  }

  private static class Curve
  {
    private const int Steps = 16;

    internal static float[] Quad(float x0, float y0, float x1, float y1, float x2, float y2)
    {
      var points = new float[(Steps + 1) * 2];
      for (var i = 0; i <= Steps; i++)
      {
        var t = i / (float)Steps;
        var a = (1 - t) * (1 - t);
        var b = 2 * (1 - t) * t;
        var c = t * t;
        points[i * 2] = a * x0 + b * x1 + c * x2;
        points[i * 2 + 1] = a * y0 + b * y1 + c * y2;
      }
      return points;
    }

    internal static float[] Cubic(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
    {
      var points = new float[(Steps + 1) * 2];
      for (var i = 0; i <= Steps; i++)
      {
        var t = i / (float)Steps;
        var a = (1 - t) * (1 - t) * (1 - t);
        var b = 3 * (1 - t) * (1 - t) * t;
        var c = 3 * (1 - t) * t * t;
        var d = t * t * t;
        points[i * 2] = a * x0 + b * x1 + c * x2 + d * x3;
        points[i * 2 + 1] = a * y0 + b * y1 + c * y2 + d * y3;
      }
      return points;
    }
  }

  private sealed class Shape
  {
    private readonly List<Func<float, float, bool>> _parts = new();

    public bool Contains(float x, float y)
    {
      foreach (var part in _parts)
      {
        if (part(x, y)) return true;
      }
      return false;
    }

    public Shape Line(float width, params float[] points)
    {
      var half = width / 2f;
      _parts.Add((x, y) =>
      {
        for (var i = 0; i + 3 < points.Length; i += 2)
        {
          if (SegmentDistance(x, y, points[i], points[i + 1], points[i + 2], points[i + 3]) <= half) return true;
        }
        return false;
      });
      return this;
    }

    public Shape Fill(params float[] points)
    {
      _parts.Add((x, y) =>
      {
        var inside = false;
        var count = points.Length / 2;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
          var xi = points[i * 2];
          var yi = points[i * 2 + 1];
          var xj = points[j * 2];
          var yj = points[j * 2 + 1];
          if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
      });
      return this;
    }

    public Shape Circle(float cx, float cy, float r)
    {
      _parts.Add((x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r);
      return this;
    }

    public Shape Rect(float left, float top, float width, float height)
    {
      _parts.Add((x, y) => x >= left && x <= left + width && y >= top && y <= top + height);
      return this;
    }

    public Shape RoundRect(float left, float top, float width, float height, float radius)
    {
      _parts.Add((x, y) =>
      {
        var dx = Mathf.Max(0f, Mathf.Max(left + radius - x, x - (left + width - radius)));
        var dy = Mathf.Max(0f, Mathf.Max(top + radius - y, y - (top + height - radius)));
        return x >= left && x <= left + width && y >= top && y <= top + height && dx * dx + dy * dy <= radius * radius;
      });
      return this;
    }

    private static float SegmentDistance(float x, float y, float x0, float y0, float x1, float y1)
    {
      var dx = x1 - x0;
      var dy = y1 - y0;
      var lengthSquared = dx * dx + dy * dy;
      var t = lengthSquared > 0f ? Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / lengthSquared) : 0f;
      var px = x0 + dx * t - x;
      var py = y0 + dy * t - y;
      return Mathf.Sqrt(px * px + py * py);
    }
  }
}
