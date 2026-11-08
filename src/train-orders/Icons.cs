using System;
using System.Collections.Generic;
using UI;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal static class Icons
{
  internal const string TrashName = "qbanrambo.train-orders.trash";
  internal const string UpName = "qbanrambo.train-orders.up";
  internal const string DownName = "qbanrambo.train-orders.down";
  internal const string CopyName = "qbanrambo.train-orders.copy";
  internal const string PasteName = "qbanrambo.train-orders.paste";
  internal const string CutName = "qbanrambo.train-orders.cut";
  internal const string WaitName = "qbanrambo.train-orders.wait";
  internal const string StationName = "qbanrambo.train-orders.station";
  internal const string RefuelName = "qbanrambo.train-orders.refuel";
  internal const string TurntableName = "qbanrambo.train-orders.turntable";
  internal const string PickUpName = "qbanrambo.train-orders.pickup";
  internal const string SpeedName = "qbanrambo.train-orders.speed";
  internal const string GoBackName = "qbanrambo.train-orders.goback";
  internal const string GotoName = "qbanrambo.train-orders.goto";

  private const int Size = 64;

  private static readonly Color32 GlyphColor = new(206, 186, 148, 255);
  private static readonly Dictionary<string, IDisposable> Registrations = new();
  private static Sprite _list;
  private static bool _warned;

  internal static Sprite List => _list != null ? _list : _list = Draw(
    Dot(14f, 18f), Line(25f, 18f, 52f, 18f),
    Dot(14f, 32f), Line(25f, 32f, 52f, 32f),
    Dot(14f, 46f), Line(25f, 46f, 52f, 46f));

  internal static bool EnsureButtons() => Ensure(TrashName, Trash) && Ensure(UpName, Up) && Ensure(DownName, Down) && Ensure(CopyName, Copy) && Ensure(PasteName, Paste)
    && Ensure(CutName, Cut) && Ensure(WaitName, Wait) && Ensure(StationName, Station) && Ensure(RefuelName, Refuel) && Ensure(TurntableName, Turntable) && Ensure(PickUpName, PickUp) && Ensure(GotoName, Goto) && Ensure(SpeedName, Speed) && Ensure(GoBackName, GoBack);

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
      if (!_warned) TrainOrdersMod.Log?.Warning($"Couldn't add the Train Orders button icons: {exception.Message}");
      _warned = true;
      return false;
    }
  }

  private static Sprite Trash() => Draw(
    new Stroke(new Vector2(11f, 49f), new Vector2(53f, 49f), 3f),
    new Stroke(new Vector2(26f, 49f), new Vector2(26f, 56f), 2.5f),
    new Stroke(new Vector2(26f, 56f), new Vector2(38f, 56f), 2.5f),
    new Stroke(new Vector2(38f, 56f), new Vector2(38f, 49f), 2.5f),
    new Stroke(new Vector2(16f, 42f), new Vector2(20f, 9f), 3f),
    new Stroke(new Vector2(48f, 42f), new Vector2(44f, 9f), 3f),
    new Stroke(new Vector2(20f, 9f), new Vector2(44f, 9f), 3f),
    new Stroke(new Vector2(27.5f, 36f), new Vector2(28f, 15f), 2.25f),
    new Stroke(new Vector2(36.5f, 36f), new Vector2(36f, 15f), 2.25f));

  private static Sprite Up() => Draw(
    new Stroke(new Vector2(10f, 20f), new Vector2(32f, 44f), 5f),
    new Stroke(new Vector2(32f, 44f), new Vector2(54f, 20f), 5f));

  private static Sprite Down() => Draw(
    new Stroke(new Vector2(10f, 44f), new Vector2(32f, 20f), 5f),
    new Stroke(new Vector2(32f, 20f), new Vector2(54f, 44f), 5f));

  private static Sprite Copy()
  {
    var strokes = new List<Stroke> { Line(13f, 24f, 13f, 53f), Line(13f, 53f, 38f, 53f) };
    strokes.AddRange(Rectangle(24f, 11f, 51f, 42f));
    return Draw(strokes.ToArray());
  }

  private static Sprite Paste()
  {
    var strokes = new List<Stroke>(Rectangle(13f, 8f, 51f, 50f))
    {
      Line(24f, 50f, 24f, 57f),
      Line(24f, 57f, 40f, 57f),
      Line(40f, 57f, 40f, 50f),
      new(new Vector2(22f, 50f), new Vector2(42f, 50f), 4f),
      new(new Vector2(22f, 36f), new Vector2(42f, 36f), 2.5f),
      new(new Vector2(22f, 25f), new Vector2(42f, 25f), 2.5f)
    };
    return Draw(strokes.ToArray());
  }

  private static Sprite Cut()
  {
    var strokes = new List<Stroke>(Rectangle(4f, 24f, 27f, 44f));
    strokes.AddRange(Rectangle(37f, 24f, 60f, 44f));
    strokes.Add(Line(27f, 31f, 29f, 31f));
    strokes.Add(Line(35f, 31f, 37f, 31f));
    strokes.Add(Dot(10f, 16f));
    strokes.Add(Dot(21f, 16f));
    strokes.Add(Dot(43f, 16f));
    strokes.Add(Dot(54f, 16f));
    return Draw(strokes.ToArray());
  }

  private static Sprite Wait()
  {
    var strokes = new List<Stroke>(Arc(32f, 32f, 22f, 0f, 360f, 36))
    {
      Line(32f, 32f, 32f, 46f),
      Line(32f, 32f, 43f, 32f),
      new(new Vector2(32f, 32f), new Vector2(32f, 32f), 3.5f)
    };
    return Draw(strokes.ToArray());
  }

  private static Sprite GoBack()
  {
    var strokes = new List<Stroke>(Arc(32f, 32f, 21f, 60f, -240f, 36))
    {
      Line(42.5f, 50.2f, 50f, 40f),
      Line(42.5f, 50.2f, 54f, 55f)
    };
    return Draw(strokes.ToArray());
  }

  private static Sprite Speed()
  {
    var strokes = new List<Stroke>(Arc(32f, 26f, 23f, 200f, -20f, 30))
    {
      Line(32f, 26f, 44f, 40f),
      new(new Vector2(32f, 26f), new Vector2(32f, 26f), 4.5f)
    };
    return Draw(strokes.ToArray());
  }

  private static Sprite Station() => Draw(
    new Stroke(new Vector2(32f, 47f), new Vector2(32f, 47f), 7.5f),
    new Stroke(new Vector2(32f, 29f), new Vector2(32f, 15f), 10.5f));

  private static Sprite Refuel()
  {
    var strokes = new List<Stroke>
    {
      Line(32f, 57f, 44.8f, 27.8f),
      Line(32f, 57f, 19.2f, 27.8f)
    };
    strokes.AddRange(Arc(32f, 22f, 14f, 24.3f, -204.3f, 24));
    return Draw(strokes.ToArray());
  }

  private static Sprite Goto()
  {
    var strokes = new List<Stroke>(Arc(32f, 40f, 15f, -38f, 218f, 30))
    {
      Line(20.2f, 30.8f, 32f, 8f),
      Line(43.8f, 30.8f, 32f, 8f),
      new(new Vector2(32f, 40f), new Vector2(32f, 40f), 5f)
    };
    return Draw(strokes.ToArray());
  }

  private static Sprite PickUp()
  {
    var strokes = new List<Stroke>(Rectangle(28f, 24f, 60f, 46f))
    {
      Line(4f, 35f, 20f, 35f),
      Line(20f, 35f, 13f, 42f),
      Line(20f, 35f, 13f, 28f),
      Dot(35f, 16f),
      Dot(53f, 16f)
    };
    return Draw(strokes.ToArray());
  }

  private static Sprite Turntable()
  {
    var strokes = new List<Stroke>(Arc(32f, 32f, 23f, 0f, 360f, 40))
    {
      Line(11f, 27f, 53f, 27f),
      Line(11f, 37f, 53f, 37f),
      Line(1f, 27f, 6f, 27f),
      Line(1f, 37f, 6f, 37f),
      Line(58f, 27f, 63f, 27f),
      Line(58f, 37f, 63f, 37f),
      new(new Vector2(32f, 32f), new Vector2(32f, 32f), 3.5f)
    };
    return Draw(strokes.ToArray());
  }

  private static IEnumerable<Stroke> Arc(float x, float y, float radius, float fromDegrees, float toDegrees, int steps)
  {
    for (var i = 0; i < steps; i++)
    {
      var a = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps);
      var b = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, (i + 1) / (float)steps);
      yield return Line(x + Mathf.Cos(a) * radius, y + Mathf.Sin(a) * radius, x + Mathf.Cos(b) * radius, y + Mathf.Sin(b) * radius);
    }
  }

  private static IEnumerable<Stroke> Rectangle(float left, float bottom, float right, float top)
  {
    yield return Line(left, bottom, right, bottom);
    yield return Line(right, bottom, right, top);
    yield return Line(right, top, left, top);
    yield return Line(left, top, left, bottom);
  }

  private static Stroke Dot(float x, float y) => new(new Vector2(x, y), new Vector2(x, y), 4.5f);

  private static Stroke Line(float x1, float y1, float x2, float y2) => new(new Vector2(x1, y1), new Vector2(x2, y2), 3f);

  private static Sprite Draw(params Stroke[] strokes)
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
    var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
    {
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSave
    };
    texture.SetPixels32(pixels);
    texture.Apply();
    var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    sprite.hideFlags = HideFlags.DontSave;
    return sprite;
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
