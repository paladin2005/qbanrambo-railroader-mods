using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Ops;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class MapNavigation
{
  private const string MenuOwner = "Map Plus";
  private const float PlaceSize = 300f;
  private const float MinTownSize = 250f;
  private const float MaxTownSize = 2500f;
  private const float TownSizeFactor = 1.2f;
  private const float MetersPerSecondToMph = 2.23694f;
  private const float FindWidth = 130f;
  private const int MaxResults = 30;
  private const int MaxCars = 15;

  private static readonly Color FoundColor = new(1f, 0.85f, 0.35f);

  private static PopupMenu _menu;
  private static string _query = "";
  private static bool _following;
  private static UnityEngine.UI.Image _followGlyph;

  internal static Car FollowTarget { get; set; }

  internal static bool Following
  {
    get => _following;
    set
    {
      _following = value;
      UpdateFollowGlyph();
    }
  }

  internal static void Reset()
  {
    _menu?.Dismiss();
    _menu = null;
    FollowTarget = null;
    Following = false;
  }

  internal static void BuildTowns(UIPanelBuilder builder)
  {
    RectTransform button = null;
    button = Button(builder, ToolbarIcons.TownsName, "Towns", () => Present(button, TownItems))
      .Tooltip("Towns", "Center the map on a town or one of its locations.")
      .RectTransform;
  }

  internal static void BuildTrains(UIPanelBuilder builder)
  {
    RectTransform button = null;
    button = Button(builder, ToolbarIcons.TrainsName, "Trains", () => Present(button, TrainItems))
      .Tooltip("Trains", "Center the map on a locomotive. Follow keeps it centered.")
      .RectTransform;
  }

  private static IConfigurableElement Button(UIPanelBuilder builder, string icon, string text, Action action)
  {
    return ToolbarIcons.Ensure() ? builder.AddIconButton(icon, action) : builder.AddButtonCompact(text, action);
  }

  private static void UpdateFollowGlyph()
  {
    try
    {
      if (_followGlyph == null) return;
      var sprite = ToolbarIcons.Get(_following ? ToolbarIcons.FollowingName : ToolbarIcons.FollowName);
      if (sprite != null) _followGlyph.sprite = sprite;
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't update the Follow button");
    }
  }

  internal static void BuildFind(UIPanelBuilder builder)
  {
    var field = builder.AddInputField(_query, text => _query = text ?? "", "Find").Width(FindWidth);
    field.Tooltip("Find", "Type a car, locomotive, industry, station or town and press Enter.");
    var input = field.GetComponent<TMP_InputField>();
    if (input != null) input.onSubmit.AddListener(text => Find(field, text));
  }

  private static void Find(RectTransform anchor, string text)
  {
    try
    {
      _query = text ?? "";
      var query = _query.Trim();
      if (query.Length == 0) return;
      var hits = Search(query);
      if (hits.Count == 0)
      {
        Toast.Present($"Nothing on the map matches \"{query}\".", ToastPosition.Bottom);
        return;
      }
      if (hits.Count == 1)
      {
        hits[0].Go();
        return;
      }
      Present(anchor, () => hits.Select(hit => MenuItem.Action(hit.Label, hit.Go, hit.Subtitle)).ToList());
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't search the map");
    }
  }

  private static List<Hit> Search(string query)
  {
    var needle = query.ToLowerInvariant();
    var hits = new List<Hit>();
    var seen = new HashSet<string>();
    void Add(int score, int group, string label, string subtitle, Action go)
    {
      if (score < 0 || !seen.Add(label + "|" + subtitle)) return;
      hits.Add(new Hit(score, group, label, subtitle, go));
    }
    foreach (var town in Places.Towns())
    {
      var area = town.Area;
      Add(Score(area.name, needle), 0, area.name, "Town", () => Go(area.Position, Mathf.Clamp(area.radius * TownSizeFactor, MinTownSize, MaxTownSize)));
      foreach (var place in town.Places)
      {
        var score = Best(Score(place.Name, needle), Score(place.Industry?.DisplayName, needle));
        Add(score, 1, place.Name, place.Name.Contains(area.name) ? "Location" : $"Location, {area.name}", () => Go(place.Position, PlaceSize));
      }
    }
    foreach (var stop in PlaceData.FuelStops())
    {
      if (stop.Loader == null) continue;
      var point = stop.Point;
      Add(Score(stop.Name, needle), 2, stop.Name, string.IsNullOrEmpty(stop.Town) ? "Fuel stop" : $"Fuel stop, {stop.Town}", () => Go(point, PlaceSize));
    }
    var cars = 0;
    foreach (var car in (TrainController.Shared?.Cars ?? Enumerable.Empty<Car>()).Where(car => car != null).OrderBy(car => car.DisplayName))
    {
      var ident = car.Ident;
      var score = Best(Score(car.DisplayName, needle), Best(Score($"{ident.ReportingMark} {ident.RoadNumber}", needle), Score(ident.RoadNumber, needle)));
      if (score < 0) continue;
      if (car is BaseLocomotive locomotive)
      {
        Add(score, 3, car.DisplayName, $"Locomotive, {Speed(locomotive)}", () =>
        {
          FollowTarget = locomotive;
          Go(locomotive.GetCenterPosition(), PlaceSize);
        });
        continue;
      }
      if (car.IsInBardo || cars >= MaxCars) continue;
      cars++;
      var model = car.DefinitionInfo?.Metadata?.Name;
      var destination = CarInfo.Destination(car)?.Describe();
      Add(score, 4, car.DisplayName, destination ?? model ?? "Car", () => Go(car.GetCenterPosition(), PlaceSize));
    }
    return hits.OrderBy(hit => hit.Score).ThenBy(hit => hit.Group).ThenBy(hit => hit.Label).Take(MaxResults).ToList();
  }

  private static int Score(string name, string needle)
  {
    if (string.IsNullOrEmpty(name)) return -1;
    var text = name.ToLowerInvariant();
    if (text == needle) return 0;
    if (text.StartsWith(needle)) return 1;
    return text.Contains(needle) ? 2 : -1;
  }

  private static int Best(int a, int b) => a < 0 ? b : b < 0 ? a : Mathf.Min(a, b);

  private static void Go(Vector3 gamePosition, float size)
  {
    Center(gamePosition, size, true);
    MapPulse.Show(gamePosition, FoundColor);
  }

  internal static void BuildWebMap(UIPanelBuilder builder)
  {
    Button(builder, ToolbarIcons.WebMapName, "Web Map", WebMap.Open)
      .Tooltip("Web Map", "Open a live map of the railroad in your browser, for a second monitor. Needs Preferences > Features > Experimental > HTTP Server.");
  }

  internal static void BuildTrackClassLegend(UIPanelBuilder builder)
  {
    var label = builder.AddLabel(() => MapPlusMod.TrackClass.IsOn ? TrackInfo.Legend() : "", UIPanelBuilder.Frequency.Periodic).GetComponent<TMP_Text>();
    if (label != null) label.textWrappingMode = TextWrappingModes.NoWrap;
  }

  internal static void BuildFollow(UIPanelBuilder builder)
  {
    if (!ToolbarIcons.Ensure())
    {
      builder.AddButtonCompact(() => Following ? "Following" : "Follow", ToggleFollow)
        .Tooltip("Follow", "Keep the map centered on the locomotive picked under Trains, or the selected one. Dragging the map stops following.");
      return;
    }
    var button = builder.AddIconButton(Following ? ToolbarIcons.FollowingName : ToolbarIcons.FollowName, ToggleFollow)
      .Tooltip("Follow", "Keep the map centered on the locomotive picked under Trains, or the selected one. Dragging the map stops following.")
      .RectTransform;
    _followGlyph = button.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
  }

  private static void Present(RectTransform anchor, Func<IReadOnlyList<MenuItem>> items)
  {
    try
    {
      _menu?.Dismiss();
      _menu = PopupMenu.PresentUnder(anchor, items(), MenuOwner);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't open the map menu");
    }
  }

  private static IReadOnlyList<MenuItem> Safe(Func<IReadOnlyList<MenuItem>> build)
  {
    try
    {
      return build();
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't build the map menu");
      return new[] { MenuItem.Action("Unavailable, see the Message Log", null, disabled: true) };
    }
  }

  private static IReadOnlyList<MenuItem> TownItems()
  {
    var towns = Places.Towns();
    if (towns.Count == 0) return new[] { MenuItem.Action("No towns available yet", null, disabled: true) };
    return towns
      .Select(town => MenuItem.Submenu(town.Area.name, () => Safe(() => PlaceItems(town.Area, town.Places))))
      .ToList();
  }

  private static IReadOnlyList<MenuItem> PlaceItems(Area area, List<Place> places)
  {
    var items = new List<MenuItem> { MenuItem.Action("Whole town", () => Center(area.Position, Mathf.Clamp(area.radius * TownSizeFactor, MinTownSize, MaxTownSize), true)) };
    items.AddRange(places.Select(place => MenuItem.Action(place.Name, () => Center(place.Position, PlaceSize, true))));
    return items;
  }

  private static IReadOnlyList<MenuItem> TrainItems()
  {
    var locomotives = TrainController.Shared?.Cars.OfType<BaseLocomotive>().Where(locomotive => locomotive != null).OrderBy(locomotive => locomotive.DisplayName).ToList();
    if (locomotives == null || locomotives.Count == 0) return new[] { MenuItem.Action("No locomotives", null, disabled: true) };
    return locomotives
      .Select(locomotive => MenuItem.Action(
        locomotive.DisplayName,
        () => PickTrain(locomotive),
        TrainSubtitle(locomotive),
        check: locomotive == FollowTarget ? DropdownMenu.CheckState.Checked : DropdownMenu.CheckState.None))
      .ToList();
  }

  private static void PickTrain(BaseLocomotive locomotive)
  {
    if (locomotive == null) return;
    FollowTarget = locomotive;
    Center(locomotive.GetCenterPosition(), null, false);
  }

  private static string TrainSubtitle(BaseLocomotive locomotive)
  {
    try
    {
      var parts = new List<string>();
      var alerts = TrainAlerts.For(locomotive);
      if (alerts.Count > 0) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(TrainAlerts.ColorFor(alerts[0].Level))}>{alerts[0].Text}</color>");
      var orders = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders;
      parts.Add(orders.Mode switch
      {
        AutoEngineerMode.Waypoint when orders.Waypoint.HasValue => $"To {WebTrain.WaypointName(orders.Waypoint.Value)}",
        AutoEngineerMode.Waypoint => "Waypoint",
        AutoEngineerMode.Road => "Road",
        AutoEngineerMode.Yard => "Yard",
        _ => "Manual"
      });
      parts.Add(Speed(locomotive));
      var cars = Consist.Of(locomotive).Count(car => car is not BaseLocomotive && !car.CarType.IsTender);
      if (cars > 0) parts.Add(cars == 1 ? "1 car" : $"{cars} cars");
      return string.Join(" · ", parts);
    }
    catch (Exception)
    {
      return Speed(locomotive);
    }
  }

  private static string Speed(BaseLocomotive locomotive)
  {
    var mph = Mathf.RoundToInt(Mathf.Abs(locomotive.velocity * MetersPerSecondToMph));
    return mph < 1 ? "Stopped" : $"{mph} mph";
  }

  private static void ToggleFollow()
  {
    try
    {
      if (Following)
      {
        Following = false;
        return;
      }
      var target = FollowTarget;
      var controller = TrainController.Shared;
      if (target == null && controller != null) target = controller.SelectedLocomotive;
      if (target == null && controller != null) target = controller.SelectedCar;
      if (target == null)
      {
        Toast.Present("Pick a locomotive under Trains, or select one, to follow it.", ToastPosition.Bottom);
        return;
      }
      FollowTarget = target;
      Following = true;
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't start following");
    }
  }

  private static void Center(Vector3 gamePosition, float? size, bool stopFollowing)
  {
    try
    {
      var builder = MapBuilder.Shared;
      if (builder == null || builder.mapCamera == null) return;
      if (stopFollowing) Following = false;
      builder.SetMapCenter(gamePosition);
      if (!size.HasValue) return;
      builder.mapCamera.orthographicSize = size.Value;
      builder.Zoom(0f, new Vector2(0.5f, 0.5f));
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't move the map");
    }
  }

  private sealed class Hit
  {
    public Hit(int score, int group, string label, string subtitle, Action go)
    {
      Score = score;
      Group = group;
      Label = label;
      Subtitle = subtitle;
      Go = go;
    }

    public int Score { get; }
    public int Group { get; }
    public string Label { get; }
    public string Subtitle { get; }
    public Action Go { get; }
  }
}

[HarmonyPatch(typeof(PopupMenu), nameof(PopupMenu.PresentUnder))]
internal static class DisplayMenuPatch
{
  private const string Owner = "MapWindow";
  private static bool _logged;

  private static void Prefix(string owner, ref IReadOnlyList<MenuItem> items)
  {
    if (owner != Owner || items == null) return;
    try
    {
      var list = items.ToList();
      var index = list.FindIndex(item => item.Label == MapPlusMod.Places.Title);
      list.Insert(index >= 0 ? index + 1 : list.Count, MenuItem.Submenu("Place Types", KindItems));
      items = list;
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't add Place Types to the Display menu");
    }
  }

  private static IReadOnlyList<MenuItem> KindItems()
  {
    try
    {
      return PlaceIcons.KindItems();
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't list the place types");
      return new[] { MenuItem.Action("Unavailable, see the Message Log", null, disabled: true) };
    }
  }
}
