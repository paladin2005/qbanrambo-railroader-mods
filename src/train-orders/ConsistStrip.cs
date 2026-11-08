using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Ops;
using TMPro;
using UI.Tags;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.TrainOrders;

internal enum CarShape
{
  Steam,
  Diesel,
  Tender,
  Box,
  Hopper,
  Tank,
  Flat,
  Gondola,
  Coach,
  Caboose,
  Other,
  Unknown
}

internal readonly struct StripCar
{
  public StripCar(CarShape shape, Color color, bool added, bool leaving)
  {
    Shape = shape;
    Color = color;
    Added = added;
    Leaving = leaving;
  }

  public CarShape Shape { get; }
  public Color Color { get; }
  public bool Added { get; }
  public bool Leaving { get; }
  public bool IsEngine => Shape is CarShape.Steam or CarShape.Diesel or CarShape.Tender;
}

internal sealed class ConsistStrip : MonoBehaviour
{
  internal const float Height = 16f;

  private const float CarHeight = 12f;
  private const float CarWidth = 16f;
  private const float EngineWidth = 26f;
  private const float Gap = 2f;
  private const float ArrowWidth = 14f;
  private const float Underline = 2f;
  private const float CountFont = 11f;
  private const float CountCharWidth = 6.5f;
  private const float CountPadding = 6f;
  private const float MinScale = 0.55f;
  private const float LeavingAlpha = 0.3f;
  private const int MaxListed = 24;

  private static readonly Vector2 ArrowSize = new(7f, 12f);

  private static readonly Color EngineColor = new(206 / 255f, 186 / 255f, 148 / 255f);
  private static readonly Color NoDestinationColor = new(0.62f, 0.62f, 0.62f);
  private static readonly Color UnknownColor = new(0.55f, 0.55f, 0.55f);
  private static readonly Color AddedColor = new(1f, 0.75f, 0.38f);
  private static readonly Color ArrowColor = new(206 / 255f, 186 / 255f, 148 / 255f);
  private static readonly Color ProblemColor = new(1f, 0.42f, 0.32f);
  private static readonly Color CountColor = new(0.78f, 0.75f, 0.7f);

  private List<StripCar> _before = new();
  private List<StripCar> _after = new();
  private bool _problem;
  private float _builtWidth = -1f;

  internal static void Add(RectTransform rect, ForecastResult result)
  {
    var ops = OpsController.Shared;
    var strip = rect.gameObject.AddComponent<ConsistStrip>();
    strip._before = result.Before.Select(car => Describe(car, ops, false, result.Leaving.Contains(car.Id))).ToList();
    strip._after = result.After.Select(car => Describe(car, ops, result.Added.Contains(car.Id), false)).ToList();
    strip._problem = result.Problem;
    var hover = rect.gameObject.AddComponent<Image>();
    hover.color = new Color(0f, 0f, 0f, 0f);
    hover.raycastTarget = true;
  }

  internal static string TooltipText(ForecastResult result)
  {
    var ops = OpsController.Shared;
    var lines = new List<string>();
    if (!string.IsNullOrEmpty(result.Note)) lines.Add(result.Problem ? $"<color=#FF6A50>{result.Note}</color>" : result.Note);
    var added = result.After.Where(car => result.Added.Contains(car.Id)).ToList();
    if (added.Count > 0) lines.Add($"Couples {Count(added.Count)}");
    var leaving = result.Before.Where(car => result.Leaving.Contains(car.Id)).ToList();
    if (leaving.Count > 0)
    {
      lines.Add($"Leaves {Count(leaving.Count)} here:");
      lines.AddRange(leaving.Take(MaxListed).Select(car => Line(car, ops, false)));
      if (leaving.Count > MaxListed) lines.Add($"and {leaving.Count - MaxListed} more");
    }
    if (lines.Count > 0) lines.Add("");
    lines.Add($"Then, from the front ({Count(result.After.Count(car => car.IsCar))}):");
    lines.AddRange(result.After.Take(MaxListed).Select(car => Line(car, ops, result.Added.Contains(car.Id))));
    if (result.After.Count > MaxListed) lines.Add($"and {result.After.Count - MaxListed} more");
    if (result.Guess) lines.Add("<alpha=#99>Which end it couples on is decided when the order runs.");
    lines.Add("<alpha=#99>A forecast from the orders as they stand.");
    return string.Join("\n", lines);
  }

  private static string Count(int count) => count == 1 ? "1 car" : $"{count} cars";

  private static string Line(ForecastCar car, OpsController ops, bool added)
  {
    if (car.IsUnknown) return $"<color=#{Hex(UnknownColor)}>■</color> a standing car{(added ? " <color=#FFC060>new</color>" : "")}";
    var parts = new List<string> { car.Car.DisplayName };
    var model = car.Car.DefinitionInfo?.Metadata?.Name;
    if (!string.IsNullOrEmpty(model)) parts.Add(model);
    if (ops != null && ops.TryGetDestinationInfo(car.Car, out var destination, out var arrived, out _, out _) && !string.IsNullOrEmpty(destination)) parts.Add(arrived ? $"at {destination}" : $"to {destination}");
    var color = Describe(car, ops, false, false).Color;
    return $"<color=#{Hex(color)}>■</color> {string.Join(", ", parts)}{(added ? " <color=#FFC060>new</color>" : "")}";
  }

  private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

  internal static Color ColorFor(ForecastCar car, OpsController ops) => Describe(car, ops, false, false).Color;

  internal static object Web(ForecastResult result)
  {
    var ops = OpsController.Shared;
    return new
    {
      b = result.Before.Select(car => WebCar(Describe(car, ops, false, result.Leaving.Contains(car.Id)))).ToList(),
      a = result.After.Select(car => WebCar(Describe(car, ops, result.Added.Contains(car.Id), false))).ToList(),
      p = result.Problem,
      tip = TooltipText(result)
    };
  }

  private static object WebCar(StripCar car) => new { k = car.Shape.ToString().ToLowerInvariant(), c = "#" + ColorUtility.ToHtmlStringRGB(car.Color), n = car.Added, l = car.Leaving };

  private static StripCar Describe(ForecastCar car, OpsController ops, bool added, bool leaving)
  {
    if (car.IsUnknown) return new StripCar(CarShape.Unknown, UnknownColor, added, leaving);
    var shape = ShapeFor(car.Car);
    Color color;
    if (shape is CarShape.Steam or CarShape.Diesel or CarShape.Tender) color = EngineColor;
    else if (ops != null && ops.TryGetDestinationInfo(car.Car, out _, out _, out _, out _))
    {
      var tag = CarTagColor.For(car.Car, ops);
      color = new Color(tag.r, tag.g, tag.b, 1f);
    }
    else color = NoDestinationColor;
    return new StripCar(shape, color, added, leaving);
  }

  private static CarShape ShapeFor(Car car)
  {
    var type = car.CarType;
    var code = type.ToString();
    if (type.IsLocomotiveSteam) return CarShape.Steam;
    if (type.IsLocomotive || car is BaseLocomotive) return CarShape.Diesel;
    if (type.IsTender) return CarShape.Tender;
    if (type.IsCoach || type.IsBaggage) return CarShape.Coach;
    if (type.IsCaboose) return CarShape.Caboose;
    if (type.IsTank) return CarShape.Tank;
    if (type.IsFlat) return CarShape.Flat;
    if (type.IsGondola) return CarShape.Gondola;
    if (code.StartsWith("H", StringComparison.Ordinal)) return CarShape.Hopper;
    if (code.StartsWith("X", StringComparison.Ordinal) || code.StartsWith("R", StringComparison.Ordinal) || code.StartsWith("S", StringComparison.Ordinal)) return CarShape.Box;
    return CarShape.Other;
  }

  private void LateUpdate()
  {
    var rect = (RectTransform)transform;
    var width = rect.rect.width;
    if (width <= 1f || Mathf.Abs(width - _builtWidth) < 0.5f) return;
    _builtWidth = width;
    try
    {
      Rebuild(rect, width);
    }
    catch (Exception exception)
    {
      _builtWidth = float.MaxValue;
      TrainOrdersMod.Log?.Error(exception, "Couldn't draw the train forecast");
    }
  }

  private void Rebuild(RectTransform rect, float width)
  {
    for (var i = rect.childCount - 1; i >= 0; i--) Destroy(rect.GetChild(i).gameObject);
    var font = FontSource();
    var side = (width - ArrowWidth) / 2f;
    Draw(rect, _before, 0f, side, font);
    var arrow = NewImage(rect, "Arrow");
    arrow.sprite = Silhouettes.Arrow();
    arrow.color = _problem ? ProblemColor : ArrowColor;
    Place(arrow.rectTransform, side + (ArrowWidth - ArrowSize.x) / 2f, Underline + 1f, ArrowSize.x, ArrowSize.y);
    Draw(rect, _after, side + ArrowWidth, side, font);
  }

  private void Draw(RectTransform parent, List<StripCar> cars, float left, float width, TMP_Text font)
  {
    var tokens = Fit(cars, width, out var scale);
    var x = left;
    foreach (var token in tokens)
    {
      if (token.Hidden > 0)
      {
        var label = $"+{token.Hidden}";
        var text = Text(parent, font, label, CountWidth(label));
        text.color = CountColor;
        Place(text.rectTransform, x, 0f, CountWidth(label), Height);
        x += CountWidth(label) + Gap;
        continue;
      }
      var car = cars[token.Index];
      var carWidth = WidthOf(car) * scale;
      var image = NewImage(parent, "Car");
      image.sprite = Silhouettes.For(car.Shape);
      image.color = car.Leaving ? new Color(car.Color.r, car.Color.g, car.Color.b, LeavingAlpha) : car.Color;
      Place(image.rectTransform, x, Underline + 1f, carWidth, CarHeight);
      if (car.Added)
      {
        var line = NewImage(parent, "New");
        line.color = AddedColor;
        Place(line.rectTransform, x - Gap / 2f, 0f, carWidth + Gap, Underline);
      }
      x += carWidth + Gap * scale;
    }
  }

  private static List<Token> Fit(List<StripCar> cars, float width, out float scale)
  {
    scale = 1f;
    var all = Enumerable.Range(0, cars.Count).Select(Token.Car).ToList();
    if (Measure(cars, all) <= width) return all;
    List<Token> tokens = null;
    for (var context = 2; context >= 0; context--)
    {
      tokens = Collapse(cars, context, false);
      if (Measure(cars, tokens) <= width) return tokens;
    }
    tokens = Collapse(cars, 0, true);
    var measured = Measure(cars, tokens);
    if (measured > width) scale = Mathf.Max(MinScale, width / measured);
    return tokens;
  }

  private static List<Token> Collapse(List<StripCar> cars, int context, bool tight)
  {
    var keep = new bool[cars.Count];
    for (var i = 0; i < cars.Count; i++)
    {
      var car = cars[i];
      if (!car.IsEngine && !car.Added && !car.Leaving) continue;
      if (tight && !car.IsEngine && RunEdgeDistance(cars, i) >= 2) continue;
      for (var j = Mathf.Max(0, i - context); j <= Mathf.Min(cars.Count - 1, i + context); j++) keep[j] = true;
    }
    var tokens = new List<Token>();
    var hidden = 0;
    for (var i = 0; i < cars.Count; i++)
    {
      if (keep[i])
      {
        if (hidden > 0) tokens.Add(Token.Gap(hidden));
        hidden = 0;
        tokens.Add(Token.Car(i));
        continue;
      }
      hidden++;
    }
    if (hidden > 0) tokens.Add(Token.Gap(hidden));
    return tokens;
  }

  private static int RunEdgeDistance(List<StripCar> cars, int index)
  {
    bool Same(int i) => i >= 0 && i < cars.Count && cars[i].Added == cars[index].Added && cars[i].Leaving == cars[index].Leaving && !cars[i].IsEngine;
    var before = 0;
    while (Same(index - before - 1)) before++;
    var after = 0;
    while (Same(index + after + 1)) after++;
    return Mathf.Min(before, after);
  }

  private static float Measure(List<StripCar> cars, List<Token> tokens)
  {
    var total = 0f;
    foreach (var token in tokens) total += (token.Hidden > 0 ? CountWidth($"+{token.Hidden}") : WidthOf(cars[token.Index])) + Gap;
    return total - Gap;
  }

  private static float WidthOf(StripCar car) => car.Shape is CarShape.Steam or CarShape.Diesel ? EngineWidth : CarWidth;

  private static float CountWidth(string label) => label.Length * CountCharWidth + CountPadding;

  private TMP_Text FontSource()
  {
    var canvas = GetComponentInParent<Canvas>();
    return canvas != null ? canvas.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.font != null) : null;
  }

  private static Image NewImage(RectTransform parent, string name)
  {
    var child = new GameObject(name, typeof(RectTransform));
    child.transform.SetParent(parent, false);
    var image = child.AddComponent<Image>();
    image.raycastTarget = false;
    return image;
  }

  private static TMP_Text Text(RectTransform parent, TMP_Text font, string value, float width)
  {
    var child = new GameObject("Count", typeof(RectTransform));
    child.transform.SetParent(parent, false);
    var text = child.AddComponent<TextMeshProUGUI>();
    if (font != null)
    {
      text.font = font.font;
      text.fontSharedMaterial = font.fontSharedMaterial;
    }
    text.fontSize = CountFont;
    text.alignment = TextAlignmentOptions.Center;
    text.textWrappingMode = TextWrappingModes.NoWrap;
    text.overflowMode = TextOverflowModes.Overflow;
    text.raycastTarget = false;
    text.text = value;
    return text;
  }

  private static void Place(RectTransform rect, float x, float y, float width, float height)
  {
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.zero;
    rect.pivot = Vector2.zero;
    rect.anchoredPosition = new Vector2(x, y);
    rect.sizeDelta = new Vector2(width, height);
  }

  private readonly struct Token
  {
    private Token(int index, int hidden)
    {
      Index = index;
      Hidden = hidden;
    }

    public int Index { get; }
    public int Hidden { get; }

    public static Token Car(int index) => new(index, 0);

    public static Token Gap(int hidden) => new(-1, hidden);
  }
}

internal static class Silhouettes
{
  private const int Scale = 4;
  private const int CarPixels = 13 * Scale;
  private const int EnginePixels = 22 * Scale;
  private const int HeightPixels = 10 * Scale;
  private const int Samples = 4;

  private const int ArrowPixels = 7 * Scale;
  private const int ArrowHeightPixels = 12 * Scale;

  private static readonly Dictionary<CarShape, Sprite> Sprites = new();
  private static Sprite _arrow;

  internal static Sprite For(CarShape shape)
  {
    if (Sprites.TryGetValue(shape, out var sprite) && sprite != null) return sprite;
    sprite = Create(shape);
    Sprites[shape] = sprite;
    return sprite;
  }

  internal static Sprite Arrow()
  {
    if (_arrow != null) return _arrow;
    var fill = new List<Shape> { Shape.Polygon(new Vector2(3, 4), new Vector2(11, 4), new Vector2(25, 24), new Vector2(11, 44), new Vector2(3, 44), new Vector2(17, 24)) };
    _arrow = Rasterize(ArrowPixels, ArrowHeightPixels, fill, new List<Shape>());
    return _arrow;
  }

  internal static void Reset()
  {
    foreach (var sprite in Sprites.Values.Append(_arrow))
    {
      if (sprite == null) continue;
      UnityEngine.Object.Destroy(sprite.texture);
      UnityEngine.Object.Destroy(sprite);
    }
    Sprites.Clear();
    _arrow = null;
  }

  private static Sprite Create(CarShape shape)
  {
    var width = shape is CarShape.Steam or CarShape.Diesel ? EnginePixels : CarPixels;
    var fill = new List<Shape>();
    var cut = new List<Shape>();
    var w = width;
    switch (shape)
    {
      case CarShape.Steam:
        fill.Add(Shape.RoundRect(12, 14, 64, 30, 7));
        fill.Add(Shape.Rect(4, 12, 15, 31));
        fill.Add(Shape.Rect(11, 29, 18, 38));
        fill.Add(Shape.Rect(34, 29, 42, 35));
        fill.Add(Shape.Rect(62, 9, 84, 36));
        fill.Add(Shape.Rect(60, 35, 86, 39));
        fill.Add(Shape.Rect(2, 7, 86, 11));
        fill.Add(Shape.Circle(22, 7, 6));
        fill.Add(Shape.Circle(37, 7, 6));
        fill.Add(Shape.Circle(52, 7, 6));
        fill.Add(Shape.Circle(75, 5, 4));
        break;
      case CarShape.Diesel:
        fill.Add(Shape.Rect(2, 8, 86, 12));
        fill.Add(Shape.Rect(4, 12, 15, 27));
        fill.Add(Shape.Rect(14, 12, 33, 37));
        fill.Add(Shape.Rect(32, 12, 84, 30));
        fill.Add(Shape.Circle(14, 5, 4));
        fill.Add(Shape.Circle(25, 5, 4));
        fill.Add(Shape.Circle(63, 5, 4));
        fill.Add(Shape.Circle(74, 5, 4));
        cut.Add(Shape.Rect(18, 27, 29, 33));
        break;
      case CarShape.Tender:
        fill.Add(Shape.Rect(3, 9, w - 3, 28));
        fill.Add(Shape.RoundRect(8, 26, w - 8, 33, 3));
        Wheels(fill, w);
        break;
      case CarShape.Box:
        fill.Add(Shape.Rect(2, 9, w - 2, 37));
        cut.Add(Shape.Rect(w / 2f - 1f, 13, w / 2f + 1f, 33));
        Wheels(fill, w);
        break;
      case CarShape.Hopper:
        fill.Add(Shape.Polygon(new Vector2(2, 36), new Vector2(w - 2, 36), new Vector2(w - 2, 22), new Vector2(w / 2f + 7, 10), new Vector2(w / 2f - 7, 10), new Vector2(2, 22)));
        Wheels(fill, w);
        break;
      case CarShape.Tank:
        fill.Add(Shape.Rect(3, 9, w - 3, 13));
        fill.Add(Shape.RoundRect(2, 12, w - 2, 30, 9));
        fill.Add(Shape.Rect(w / 2f - 5, 28, w / 2f + 5, 35));
        Wheels(fill, w);
        break;
      case CarShape.Flat:
        fill.Add(Shape.Rect(1, 10, w - 1, 16));
        Wheels(fill, w);
        break;
      case CarShape.Gondola:
        fill.Add(Shape.Rect(2, 9, w - 2, 24));
        Wheels(fill, w);
        break;
      case CarShape.Coach:
        fill.Add(Shape.RoundRect(2, 9, w - 2, 37, 6));
        for (var x = 8f; x + 5f < w - 6f; x += 9f) cut.Add(Shape.Rect(x, 24, x + 5f, 31));
        Wheels(fill, w);
        break;
      case CarShape.Caboose:
        fill.Add(Shape.Rect(3, 9, w - 3, 30));
        fill.Add(Shape.Rect(w / 2f - 9, 29, w / 2f + 9, 39));
        Wheels(fill, w);
        break;
      case CarShape.Unknown:
        fill.Add(Shape.RoundRect(2, 9, w - 2, 37, 4));
        cut.Add(Shape.RoundRect(6, 13, w - 6, 33, 2));
        Wheels(fill, w);
        break;
      default:
        fill.Add(Shape.RoundRect(2, 9, w - 2, 34, 4));
        Wheels(fill, w);
        break;
    }
    return Rasterize(width, HeightPixels, fill, cut);
  }

  private static Sprite Rasterize(int width, int height, List<Shape> fill, List<Shape> cut)
  {
    var pixels = new Color32[width * height];
    for (var y = 0; y < height; y++)
    {
      for (var x = 0; x < width; x++)
      {
        var covered = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
          for (var sx = 0; sx < Samples; sx++)
          {
            var point = new Vector2(x + (sx + 0.5f) / Samples, y + (sy + 0.5f) / Samples);
            if (fill.Any(item => item.Contains(point)) && !cut.Any(item => item.Contains(point))) covered++;
          }
        }
        pixels[y * width + x] = new Color32(255, 255, 255, (byte)(covered * 255 / (Samples * Samples)));
      }
    }
    var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
    {
      filterMode = FilterMode.Trilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSave
    };
    texture.SetPixels32(pixels);
    texture.Apply(true);
    var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
    sprite.hideFlags = HideFlags.DontSave;
    return sprite;
  }

  private static void Wheels(List<Shape> fill, float width)
  {
    fill.Add(Shape.Circle(9, 5, 4));
    fill.Add(Shape.Circle(width - 9, 5, 4));
  }

  private sealed class Shape
  {
    private readonly Func<Vector2, bool> _contains;

    private Shape(Func<Vector2, bool> contains)
    {
      _contains = contains;
    }

    public bool Contains(Vector2 point) => _contains(point);

    public static Shape Rect(float left, float bottom, float right, float top) => new(p => p.x >= left && p.x <= right && p.y >= bottom && p.y <= top);

    public static Shape Circle(float x, float y, float radius) => new(p => (p - new Vector2(x, y)).sqrMagnitude <= radius * radius);

    public static Shape RoundRect(float left, float bottom, float right, float top, float radius)
    {
      return new Shape(p =>
      {
        var cx = Mathf.Clamp(p.x, left + radius, right - radius);
        var cy = Mathf.Clamp(p.y, bottom + radius, top - radius);
        return (p - new Vector2(cx, cy)).sqrMagnitude <= radius * radius;
      });
    }

    public static Shape Polygon(params Vector2[] points)
    {
      return new Shape(p =>
      {
        var inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
          var a = points[i];
          var b = points[j];
          if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
      });
    }
  }
}
