using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Helpers;
using Model;
using Model.Ops;
using TMPro;
using UI;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.MapPlus;

internal sealed class PlaceIcons : MonoBehaviour
{
  private const float RefreshSeconds = 3f;
  private const float Height = 60f;
  private const float PlaceUnits = 22f;
  private const float FuelUnits = 17f;
  private const float FuelMaxSize = 1500f;
  private const float CountsMaxSize = 4000f;
  private const float Spacing = 1.1f;
  private const float CountsFont = 30f;
  private const float PillHeight = 38f;
  private const float PillPadding = 20f;
  private const int DestinationsShown = 3;
  private const string PreferencePrefix = "qbanrambo.map-plus.places.";
  private const string RiderColor = "8CC8FF";
  private const string ComingColor = "F1B827";
  private const string WarningColor = "FF9A3C";
  private const string EmptyColor = "FF5A50";

  internal static readonly (string Key, string Label)[] Kinds =
  {
    ("mine", "Mines"),
    ("logging", "Logging"),
    ("mill", "Mills"),
    ("freight", "Freight customers"),
    ("oil", "Fuel dealers"),
    ("mow", "Track materials"),
    ("interchange", "Interchanges"),
    ("station", "Passenger stations"),
    ("engine", "Engine terminals"),
    ("fuel", "Fuel stops")
  };

  private static readonly Dictionary<string, string> KindNames = new()
  {
    { "mine", "Mine" },
    { "logging", "Logging" },
    { "mill", "Mill" },
    { "freight", "Freight customer" },
    { "oil", "Fuel dealer" },
    { "mow", "Track materials" },
    { "interchange", "Interchange" },
    { "station", "Passenger station" },
    { "engine", "Engine terminal" },
    { "water", "Water" },
    { "coal", "Coal" },
    { "diesel", "Diesel" }
  };

  private static readonly List<Poi> Pois = new();
  private static readonly Dictionary<string, bool> Shown = new();
  private static bool _dirty;

  private GameObject _root;
  private TMP_Text _fontSource;
  private float _nextRefresh;
  private float _lastSize = -1f;
  private float _lastUnit = -1f;
  private bool _logged;

  internal static bool IsShown(string kind)
  {
    if (Shown.TryGetValue(kind, out var shown)) return shown;
    shown = GlobalPreferences.GetInt(PreferencePrefix + kind, 1) != 0;
    Shown[kind] = shown;
    return shown;
  }

  internal static IReadOnlyList<MenuItem> KindItems()
  {
    var items = Kinds
      .Select(kind => MenuItem.Action(kind.Label, () => SetShown(kind.Key, !IsShown(kind.Key)), check: IsShown(kind.Key) ? DropdownMenu.CheckState.Checked : DropdownMenu.CheckState.Unchecked))
      .ToList();
    items.Add(MenuItem.Action("Show all", () =>
    {
      foreach (var kind in Kinds) SetShown(kind.Key, true);
    }, disabled: Kinds.All(kind => IsShown(kind.Key))));
    return items;
  }

  private static void SetShown(string kind, bool shown)
  {
    Shown[kind] = shown;
    GlobalPreferences.SetInt(PreferencePrefix + kind, shown ? 1 : 0);
    GlobalPreferences.Flush();
    _dirty = true;
  }

  private const float HoverSeconds = 0.3f;

  private static Poi _hovered;
  private static float _hoveredAt;

  internal static Poi Hovered => _hovered != null && Time.unscaledTime - _hoveredAt < HoverSeconds ? _hovered : null;

  internal static void MarkHovered(Poi poi)
  {
    _hovered = poi;
    _hoveredAt = Time.unscaledTime;
  }

  internal static Poi At(Vector3 gamePoint)
  {
    if (!MapPlusMod.Places.IsOn) return null;
    Poi best = null;
    var bestDistance = float.MaxValue;
    foreach (var poi in Pois)
    {
      if (!poi.Visible) continue;
      var distance = Vector2.Distance(new Vector2(poi.Position.x, poi.Position.z), new Vector2(gamePoint.x, gamePoint.z));
      if (distance > poi.Radius || distance >= bestDistance) continue;
      bestDistance = distance;
      best = poi;
    }
    return best;
  }

  internal static TooltipInfo Tooltip(Poi poi)
  {
    var lines = new List<string>();
    if (poi.Stop != null)
    {
      lines.Add(Join(KindName(poi.Kind), poi.Stop.Town));
      var stock = PlaceData.Stock(poi.Stop, out var empty);
      if (empty) lines.Add($"<color=#{EmptyColor}>Out of stock</color>");
      else if (stock != null) lines.Add($"{stock} in stock");
      var source = string.IsNullOrEmpty(poi.Stop.Source) ? null : OpsController.Shared?.IndustryForId(poi.Stop.Source);
      if (source != null) lines.Add($"Supplied by {source.DisplayName}");
    }
    else if (poi.Industry != null)
    {
      IndustryLines(poi, lines);
    }
    if (CanOpen(poi, out _)) lines.Add("<alpha=#99>Click to see it in Company > Locations");
    return new TooltipInfo(poi.Name, string.Join("\n", lines));
  }

  private static void IndustryLines(Poi poi, List<string> lines)
  {
    var industry = poi.Industry;
    lines.Add(Join(KindName(poi.Kind), poi.Town));
    if (industry.usesContract && industry.Contract.HasValue)
    {
      var tier = industry.Contract.Value.Tier;
      var next = industry.NextContract?.Tier;
      lines.Add(next.HasValue && next.Value != tier ? $"Contract tier {tier}, tier {next.Value} next" : $"Contract tier {tier}");
    }
    var (makes, takes) = PlaceData.Loads(industry);
    if (makes.Count > 0) lines.Add("Ships " + string.Join(", ", makes.OrderBy(name => name)));
    if (takes.Count > 0) lines.Add("Receives " + string.Join(", ", takes.OrderBy(name => name)));
    var tracks = poi.Tracks.Where(track => track.Spotted > 0 || track.Coming > 0).ToList();
    foreach (var track in tracks)
    {
      var counts = Cars(track.Spotted, track.Coming);
      lines.Add(poi.Tracks.Count > 1 ? $"{track.Name}: {counts}" : counts);
    }
    var stop = industry.PassengerStop;
    if (stop != null && stop.IsEnabledReachable)
    {
      var waiting = stop.Waiting.Where(entry => entry.Value.Total > 0).OrderByDescending(entry => entry.Value.Total).ToList();
      var total = waiting.Sum(entry => entry.Value.Total);
      if (total > 0)
      {
        var destinations = string.Join(", ", waiting.Take(DestinationsShown).Select(entry => $"{PassengerStop.NameForIdentifier(entry.Key)} {entry.Value.Total}"));
        if (waiting.Count > DestinationsShown) destinations += ", …";
        lines.Add($"<color=#{RiderColor}>{total} {(total == 1 ? "passenger" : "passengers")} waiting</color>: {destinations}");
      }
      else
      {
        lines.Add("No passengers waiting");
      }
      var last = stop.LastServed;
      if (last.HasValue) lines.Add($"Last served {Ago(TimeWeather.Now.TotalSeconds - last.Value.TotalSeconds)}");
    }
    var warnings = industry.Storage?.Warnings?.Values;
    if (warnings == null) return;
    foreach (var warning in warnings.Where(warning => !string.IsNullOrWhiteSpace(warning))) lines.Add($"<color=#{WarningColor}>{warning}</color>");
  }

  internal static void Open(Poi poi)
  {
    try
    {
      if (!CanOpen(poi, out var id)) return;
      UI.CompanyWindow.CompanyWindow.Shared.ShowIndustry(id);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, $"Couldn't open {poi?.Name}");
    }
  }

  private static bool CanOpen(Poi poi, out string id)
  {
    id = null;
    var industry = poi?.Industry;
    if (industry == null && !string.IsNullOrEmpty(poi?.Stop?.Source)) industry = OpsController.Shared?.IndustryForId(poi.Stop.Source);
    if (industry == null || !industry.IsProgressionEnabled || !industry.TrackDisplayables.Any()) return false;
    id = industry.identifier;
    return !string.IsNullOrEmpty(id);
  }

  private void OnEnable()
  {
    MapPlusMod.Places.Changed += OnOptionChanged;
  }

  private void OnDisable()
  {
    MapPlusMod.Places.Changed -= OnOptionChanged;
  }

  private void OnDestroy()
  {
    Pois.Clear();
    if (_root != null) Destroy(_root);
  }

  private void OnOptionChanged(bool _) => _nextRefresh = 0f;

  private void Update()
  {
    try
    {
      var mapShown = MapMouse.IsMapShown(out var camera);
      var shown = mapShown && MapPlusMod.Places.IsOn;
      if (_root != null && _root.activeSelf != shown) _root.SetActive(shown);
      if (!shown) return;
      if (Time.unscaledTime >= _nextRefresh)
      {
        _nextRefresh = Time.unscaledTime + RefreshSeconds;
        Refresh();
        _dirty = true;
      }
      if (_root == null) return;
      _root.transform.SetPositionAndRotation(WorldTransformer.GameToWorld(Vector3.zero), Quaternion.Euler(90f, 0f, 0f));
      var size = camera.orthographicSize;
      var unit = MapMouse.MetersPerUnit(camera);
      if (!_dirty && Mathf.Approximately(size, _lastSize) && Mathf.Approximately(unit, _lastUnit)) return;
      _dirty = false;
      _lastSize = size;
      _lastUnit = unit;
      Layout(size, unit);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't draw the places");
    }
  }

  private void Refresh()
  {
    var fresh = new Dictionary<string, Poi>();
    var counts = PlaceData.CountCars();
    var engines = PlaceData.Engines();
    var empty = new CarBucket();
    foreach (var town in Places.Towns())
    {
      foreach (var group in town.Places.Where(place => place.Industry != null).GroupBy(place => place.Industry))
      {
        var industry = group.Key;
        var key = industry.identifier ?? industry.DisplayName;
        if (string.IsNullOrEmpty(key) || fresh.ContainsKey(key)) continue;
        var places = group.ToList();
        var center = Vector3.zero;
        foreach (var place in places) center += place.Position;
        center /= places.Count;
        var tracks = places.Select(place =>
        {
          var bucket = counts.TryGetValue(place.SpanKey, out var found) ? found : empty;
          return new PoiTrack(place.Name, bucket.Spotted.Count, bucket.Coming.Count);
        }).ToList();
        var buckets = places.Select(place => counts.TryGetValue(place.SpanKey, out var found) ? found : empty).ToList();
        var stop = industry.PassengerStop;
        var riders = stop != null && stop.IsEnabledReachable ? stop.Waiting.Values.Sum(info => info.Total) : 0;
        fresh[key] = new Poi
        {
          Key = key,
          Kind = PlaceData.Category(industry, engines),
          Name = industry.DisplayName,
          Town = town.Area.name,
          Industry = industry,
          Position = center,
          Tracks = tracks,
          Spotted = tracks.Sum(track => track.Spotted),
          Coming = tracks.Sum(track => track.Coming),
          Riders = riders,
          SpottedCars = buckets.SelectMany(bucket => bucket.Spotted).Distinct().ToList(),
          ComingCars = buckets.SelectMany(bucket => bucket.Coming).Distinct().ToList()
        };
      }
    }
    foreach (var stop in PlaceData.FuelStops())
    {
      if (stop.Loader == null) continue;
      var key = "fuel:" + stop.Id;
      if (fresh.ContainsKey(key)) continue;
      PlaceData.Stock(stop, out var outOfStock);
      fresh[key] = new Poi { Key = key, Kind = stop.Kind, Name = stop.Name, Town = stop.Town, Stop = stop, Position = stop.Point, Empty = outOfStock };
    }
    Build();
    for (var i = Pois.Count - 1; i >= 0; i--)
    {
      var poi = Pois[i];
      if (fresh.TryGetValue(poi.Key, out var update) && poi.Rect != null)
      {
        poi.CopyFrom(update);
        fresh.Remove(poi.Key);
        UpdateVisuals(poi);
        continue;
      }
      if (poi.Rect != null) Destroy(poi.Rect.gameObject);
      Pois.RemoveAt(i);
    }
    foreach (var poi in fresh.Values)
    {
      CreateVisuals(poi);
      UpdateVisuals(poi);
      Pois.Add(poi);
    }
  }

  private void Layout(float size, float unit)
  {
    var fuelShown = size <= FuelMaxSize && IsShown("fuel");
    var countsShown = size <= CountsMaxSize;
    var placed = new List<Poi>();
    foreach (var poi in Pois.OrderBy(poi => poi.Stop != null ? 1 : 0).ThenByDescending(poi => poi.Spotted + poi.Coming + poi.Riders))
    {
      var visible = poi.Stop != null ? fuelShown : IsShown(poi.Kind);
      var radius = (poi.Stop != null ? FuelUnits : PlaceUnits) * unit / 2f;
      if (visible)
      {
        foreach (var other in placed)
        {
          var distance = Vector2.Distance(new Vector2(poi.Position.x, poi.Position.z), new Vector2(other.Position.x, other.Position.z));
          if (distance >= (radius + other.Radius) * Spacing) continue;
          visible = false;
          break;
        }
      }
      poi.Visible = visible;
      poi.Radius = radius;
      if (poi.Rect.gameObject.activeSelf != visible) poi.Rect.gameObject.SetActive(visible);
      if (!visible) continue;
      placed.Add(poi);
      poi.Rect.localScale = Vector3.one * (radius * 2f / Glyphs.Size);
      poi.Rect.localPosition = new Vector3(poi.Position.x, poi.Position.z, -(poi.Position.y + Height));
      var counts = countsShown && !string.IsNullOrEmpty(poi.CountsText);
      if (poi.Pill.gameObject.activeSelf != counts) poi.Pill.gameObject.SetActive(counts);
    }
  }

  private void Build()
  {
    if (_root != null) return;
    _root = new GameObject("Map Plus Places", typeof(RectTransform)) { layer = Layers.Map };
    _root.transform.SetParent(transform, false);
    var canvas = _root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.WorldSpace;
    var prefab = TrainController.Shared != null ? TrainController.Shared.locomotiveMapIconPrefab : null;
    var prefabCanvas = prefab != null ? prefab.GetComponent<Canvas>() : null;
    if (prefabCanvas != null)
    {
      canvas.sortingLayerID = prefabCanvas.sortingLayerID;
      canvas.sortingOrder = prefabCanvas.sortingOrder;
    }
    _fontSource = prefab != null ? prefab.GetComponentInChildren<TMP_Text>(true) : null;
  }

  private void CreateVisuals(Poi poi)
  {
    var iconObject = new GameObject("Place", typeof(RectTransform)) { layer = Layers.Map };
    var rect = (RectTransform)iconObject.transform;
    rect.SetParent(_root.transform, false);
    rect.sizeDelta = new Vector2(Glyphs.Size, Glyphs.Size);
    var image = iconObject.AddComponent<Image>();
    image.raycastTarget = false;
    var collider = iconObject.AddComponent<BoxCollider>();
    collider.size = new Vector3(Glyphs.Size, Glyphs.Size, 1f);
    iconObject.AddComponent<PlaceClick>().Poi = poi;

    var pillObject = new GameObject("Counts", typeof(RectTransform)) { layer = Layers.Map };
    var pill = (RectTransform)pillObject.transform;
    pill.SetParent(rect, false);
    pill.anchorMin = pill.anchorMax = new Vector2(1f, 0.5f);
    pill.pivot = new Vector2(0f, 0.5f);
    pill.anchoredPosition = new Vector2(2f, 0f);
    var pillImage = pillObject.AddComponent<Image>();
    pillImage.sprite = Glyphs.Pill;
    pillImage.type = Image.Type.Sliced;
    pillImage.raycastTarget = false;

    var textObject = new GameObject("Text", typeof(RectTransform)) { layer = Layers.Map };
    var textRect = (RectTransform)textObject.transform;
    textRect.SetParent(pill, false);
    textRect.anchorMin = Vector2.zero;
    textRect.anchorMax = Vector2.one;
    textRect.offsetMin = Vector2.zero;
    textRect.offsetMax = Vector2.zero;
    var text = textObject.AddComponent<TextMeshProUGUI>();
    if (_fontSource != null)
    {
      text.font = _fontSource.font;
      text.fontSharedMaterial = _fontSource.fontSharedMaterial;
    }
    text.fontSize = CountsFont;
    text.fontStyle = FontStyles.Bold;
    text.color = new Color(0.96f, 0.94f, 0.89f);
    text.alignment = TextAlignmentOptions.Center;
    text.textWrappingMode = TextWrappingModes.NoWrap;
    text.overflowMode = TextOverflowModes.Overflow;
    text.raycastTarget = false;
    text.richText = true;

    poi.Rect = rect;
    poi.Image = image;
    poi.Pill = pill;
    poi.Text = text;
    iconObject.SetActive(false);
  }

  private static void UpdateVisuals(Poi poi)
  {
    var sprite = Glyphs.Place(poi.Kind, poi.Empty);
    if (poi.Image.sprite != sprite) poi.Image.sprite = sprite;
    var counts = Counts(poi);
    if (counts == poi.CountsText) return;
    poi.CountsText = counts;
    if (string.IsNullOrEmpty(counts))
    {
      poi.Pill.gameObject.SetActive(false);
      return;
    }
    poi.Text.text = counts;
    var width = poi.Text.GetPreferredValues(counts).x;
    poi.Pill.sizeDelta = new Vector2(width + PillPadding, PillHeight);
  }

  private static string Counts(Poi poi)
  {
    var parts = new List<string>();
    if (poi.Riders > 0) parts.Add($"<color=#{RiderColor}>{poi.Riders}</color>");
    if (poi.Spotted > 0 || poi.Coming > 0) parts.Add(poi.Coming > 0 ? $"{poi.Spotted} <color=#{ComingColor}>+{poi.Coming}</color>" : poi.Spotted.ToString());
    return string.Join("  ", parts);
  }

  private static string Cars(int spotted, int coming)
  {
    var parts = new List<string>();
    if (spotted > 0) parts.Add($"{spotted} {(spotted == 1 ? "car" : "cars")} spotted");
    if (coming > 0) parts.Add($"<color=#{ComingColor}>{coming} on the way</color>");
    return string.Join(", ", parts);
  }

  private static string KindName(string kind) => KindNames.TryGetValue(kind, out var name) ? name : "Industry";

  private static string Join(string first, string second) => string.IsNullOrEmpty(second) ? first : $"{first}, {second}";

  private static string Ago(double seconds)
  {
    var hours = Math.Max(0, seconds) / 3600.0;
    if (hours < 1) return $"{Math.Max(1, (int)Math.Round(hours * 60))} min ago";
    if (hours < 48) return $"{(int)Math.Round(hours)} h ago";
    return $"{(int)Math.Round(hours / 24)} days ago";
  }

  internal sealed class Poi
  {
    public string Key;
    public string Kind;
    public string Name;
    public string Town;
    public Industry Industry;
    public FuelStop Stop;
    public Vector3 Position;
    public List<PoiTrack> Tracks = new();
    public int Spotted;
    public int Coming;
    public int Riders;
    public List<Car> SpottedCars = new();
    public List<Car> ComingCars = new();
    public bool Empty;
    public RectTransform Rect;
    public Image Image;
    public RectTransform Pill;
    public TMP_Text Text;
    public string CountsText;
    public bool Visible;
    public float Radius;

    public void CopyFrom(Poi other)
    {
      Kind = other.Kind;
      Name = other.Name;
      Town = other.Town;
      Industry = other.Industry;
      Stop = other.Stop;
      Position = other.Position;
      Tracks = other.Tracks;
      Spotted = other.Spotted;
      Coming = other.Coming;
      Riders = other.Riders;
      SpottedCars = other.SpottedCars;
      ComingCars = other.ComingCars;
      Empty = other.Empty;
    }
  }

  internal readonly struct PoiTrack
  {
    public PoiTrack(string name, int spotted, int coming)
    {
      Name = name;
      Spotted = spotted;
      Coming = coming;
    }

    public string Name { get; }
    public int Spotted { get; }
    public int Coming { get; }
  }
}

internal sealed class PlaceClick : MonoBehaviour, IMapClickable
{
  internal PlaceIcons.Poi Poi;

  public void Click() => PlaceIcons.Open(Poi);
}
