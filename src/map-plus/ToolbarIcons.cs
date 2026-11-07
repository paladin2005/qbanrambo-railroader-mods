using System;
using System.Collections.Generic;
using UI;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class ToolbarIcons
{
  internal const string TownsName = "qbanrambo.map-plus.towns";
  internal const string TrainsName = "qbanrambo.map-plus.trains";
  internal const string FollowName = "qbanrambo.map-plus.follow";
  internal const string FollowingName = "qbanrambo.map-plus.following";
  internal const string WebMapName = "qbanrambo.map-plus.web-map";

  private const int Size = 64;
  private const float Width = 3f;

  private static readonly Color32 GlyphColor = new(206, 186, 148, 255);
  private static readonly Dictionary<string, IDisposable> Registrations = new();
  private static bool _warned;

  internal static bool Ensure() => Ensure(TownsName, Towns) && Ensure(TrainsName, Trains) && Ensure(FollowName, () => Follow(false)) && Ensure(FollowingName, () => Follow(true)) && Ensure(WebMapName, WebMap);

  internal static Sprite Get(string name) => SpriteLibrary.TryGet(name, out var sprite) ? sprite : null;

  internal static void Reset()
  {
    foreach (var registration in Registrations.Values) registration.Dispose();
    Registrations.Clear();
  }

  private static bool Ensure(string name, Func<Sprite> draw)
  {
    try
    {
      if (SpriteLibrary.TryGet(name, out var registered) && registered != null) return true;
      if (Registrations.TryGetValue(name, out var stale)) stale.Dispose();
      Registrations[name] = SpriteLibrary.Register(name, draw());
      return true;
    }
    catch (Exception exception)
    {
      if (!_warned) MapPlusMod.Log?.Warning($"Couldn't add the Map Plus toolbar icons: {exception.Message}");
      _warned = true;
      return false;
    }
  }

  private static Sprite Towns()
  {
    var strokes = new List<Stroke>();
    strokes.AddRange(Polyline(8f, 10f, 8f, 30f, 19f, 41f, 30f, 30f, 30f, 10f, 8f, 10f));
    strokes.AddRange(Polyline(30f, 10f, 56f, 10f, 56f, 50f, 36f, 50f, 36f, 31f));
    strokes.Add(Line(16f, 10f, 16f, 20f));
    strokes.Add(Line(16f, 20f, 22f, 20f));
    strokes.Add(Line(22f, 20f, 22f, 10f));
    strokes.Add(Dot(43f, 41f, 2.6f));
    strokes.Add(Dot(50f, 41f, 2.6f));
    strokes.Add(Dot(43f, 31f, 2.6f));
    strokes.Add(Dot(50f, 31f, 2.6f));
    strokes.Add(Dot(43f, 21f, 2.6f));
    strokes.Add(Dot(50f, 21f, 2.6f));
    return Draw(strokes);
  }

  private static Sprite Trains()
  {
    var strokes = new List<Stroke>();
    strokes.AddRange(Polyline(8f, 20f, 8f, 36f, 36f, 36f));
    strokes.AddRange(Polyline(36f, 20f, 36f, 50f, 56f, 50f, 56f, 20f));
    strokes.Add(Line(6f, 20f, 58f, 20f));
    strokes.Add(Line(15f, 36f, 15f, 46f));
    strokes.Add(Line(11f, 46f, 19f, 46f));
    strokes.Add(Line(41f, 42f, 51f, 42f));
    strokes.Add(Dot(16f, 12f, 5f));
    strokes.Add(Dot(30f, 12f, 5f));
    strokes.Add(Dot(47f, 12f, 5f));
    return Draw(strokes);
  }

  private static Sprite Follow(bool following)
  {
    var strokes = new List<Stroke>();
    strokes.AddRange(Ring(32f, 32f, 17f, 28));
    strokes.Add(Line(32f, 4f, 32f, 15f));
    strokes.Add(Line(32f, 49f, 32f, 60f));
    strokes.Add(Line(4f, 32f, 15f, 32f));
    strokes.Add(Line(49f, 32f, 60f, 32f));
    strokes.Add(Dot(32f, 32f, following ? 9f : 4f));
    return Draw(strokes);
  }

  private static Sprite WebMap()
  {
    var strokes = new List<Stroke>();
    strokes.AddRange(Polyline(6f, 20f, 6f, 54f, 58f, 54f, 58f, 20f, 6f, 20f));
    strokes.Add(Line(32f, 20f, 32f, 12f));
    strokes.Add(Line(20f, 10f, 44f, 10f));
    strokes.AddRange(Polyline(13f, 29f, 23f, 42f, 35f, 31f, 51f, 46f));
    return Draw(strokes);
  }

  private static IEnumerable<Stroke> Polyline(params float[] points)
  {
    for (var i = 0; i + 3 < points.Length; i += 2) yield return Line(points[i], points[i + 1], points[i + 2], points[i + 3]);
  }

  private static IEnumerable<Stroke> Ring(float x, float y, float radius, int steps)
  {
    for (var i = 0; i < steps; i++)
    {
      var a = i * Mathf.PI * 2f / steps;
      var b = (i + 1) * Mathf.PI * 2f / steps;
      yield return Line(x + Mathf.Cos(a) * radius, y + Mathf.Sin(a) * radius, x + Mathf.Cos(b) * radius, y + Mathf.Sin(b) * radius);
    }
  }

  private static Stroke Dot(float x, float y, float radius) => new(new Vector2(x, y), new Vector2(x, y), radius);

  private static Stroke Line(float x1, float y1, float x2, float y2) => new(new Vector2(x1, y1), new Vector2(x2, y2), Width);

  private static Sprite Draw(List<Stroke> strokes)
  {
    var pixels = new Color32[Size * Size];
    for (var y = 0; y < Size; y++)
    {
      for (var x = 0; x < Size; x++)
      {
        var point = new Vector2(x + 0.5f, y + 0.5f);
        var coverage = 0f;
        foreach (var stroke in strokes) coverage = Mathf.Max(coverage, Mathf.Clamp01(stroke.Radius - stroke.Distance(point) + 0.5f));
        pixels[y * Size + x] = new Color32(GlyphColor.r, GlyphColor.g, GlyphColor.b, (byte)(coverage * 255f));
      }
    }
    return Sprites.Create(Size, pixels);
  }

  private readonly struct Stroke
  {
    public Stroke(Vector2 from, Vector2 to, float radius)
    {
      From = from;
      To = to;
      Radius = radius;
    }

    public Vector2 From { get; }
    public Vector2 To { get; }
    public float Radius { get; }

    public float Distance(Vector2 point)
    {
      var along = To - From;
      if (along.sqrMagnitude < 0.0001f) return Vector2.Distance(point, From);
      var t = Mathf.Clamp01(Vector2.Dot(point - From, along) / along.sqrMagnitude);
      return Vector2.Distance(point, From + along * t);
    }
  }
}
