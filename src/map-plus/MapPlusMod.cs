using System;
using System.Collections.Generic;
using HarmonyLib;
using Railroader.Modding;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

public sealed class MapPlusMod : IModEntryPoint, IWorldLifecycle
{
  internal static IModLogger Log;
  internal static readonly MapDisplayOption Terrain = new("qbanrambo.map-plus.terrain", "Terrain", true);
  internal static readonly MapDisplayOption Cars = new("qbanrambo.map-plus.cars", "Cars", true);
  internal static readonly MapDisplayOption CarLabels = new("qbanrambo.map-plus.car-labels", "Car Labels", true);
  internal static readonly MapDisplayOption CarAlerts = new("qbanrambo.map-plus.car-alerts", "Car Alerts", true);
  internal static readonly MapDisplayOption IndustryTracks = new("qbanrambo.map-plus.industry-tracks", "Industry Tracks", true);
  internal static readonly MapDisplayOption Mainline = new("qbanrambo.map-plus.mainline", "Mainline", true);
  internal static readonly MapDisplayOption TrackClass = new("qbanrambo.map-plus.track-class", "Track Class", false);
  internal static readonly MapDisplayOption TrainRoute = new("qbanrambo.map-plus.train-route", "Train Route", true);
  internal static readonly MapDisplayOption Signals = new("qbanrambo.map-plus.signals", "Signals", true);
  internal static readonly MapDisplayOption Fusees = new("qbanrambo.map-plus.fusees", "Fusees", true);
  internal static readonly MapDisplayOption TrainAlerts = new("qbanrambo.map-plus.train-alerts", "Train Alerts", true);
  internal static readonly MapDisplayOption Places = new("qbanrambo.map-plus.places", "Places", true);
  internal static readonly MapDisplayOption TrainDestinations = new("qbanrambo.map-plus.train-destinations", "Train Destinations", true);
  internal static readonly MapDisplayOption TrainOrders = new("qbanrambo.map-plus.train-orders", "Train Orders", true);

  private readonly List<IDisposable> _registrations = new();
  private Harmony _harmony;
  private GameObject _host;

  internal static bool MapEnabled { get; private set; } = true;

  public void OnModLoad(IModContext context)
  {
    Log = context.Log;
    MapPlusConfig.Load();
    MapEnabled = MapPlusConfig.MapInGame;
    _harmony = new Harmony(context.ModIdentifier);
    if (MapEnabled) AddToMap();
    else Log.Info($"Map Plus leaves the game's map alone (EnableMapInGame=0 in {MapPlusConfig.FilePath}); only the web map runs");
    WebMap.Start(context.ModDirectory);
    ModsTab.Add(_harmony, MapPlusConfig.Draw, context.Log);
    UpdateCheck.Register("Map Plus", 1894, context.Log);
    if (UpdateCheck.IsOwner) ModsTab.Add(_harmony, UpdateCheck.DrawSection, context.Log);
    Log.Info("Map Plus ready");
  }

  private void AddToMap()
  {
    _registrations.Add(MapDisplayOptions.Add(Terrain));
    _registrations.Add(MapDisplayOptions.Add(Cars));
    _registrations.Add(MapDisplayOptions.Add(CarLabels));
    _registrations.Add(MapDisplayOptions.Add(CarAlerts));
    _registrations.Add(MapDisplayOptions.Add(IndustryTracks));
    _registrations.Add(MapDisplayOptions.Add(Mainline));
    _registrations.Add(MapDisplayOptions.Add(TrackClass));
    _registrations.Add(MapDisplayOptions.Add(TrainRoute));
    _registrations.Add(MapDisplayOptions.Add(Signals));
    _registrations.Add(MapDisplayOptions.Add(Fusees));
    _registrations.Add(MapDisplayOptions.Add(TrainAlerts));
    _registrations.Add(MapDisplayOptions.Add(Places));
    _registrations.Add(MapDisplayOptions.Add(TrainDestinations));
    _registrations.Add(MapDisplayOptions.Add(TrainOrders));
    _registrations.Add(MapToolbar.Add(MapToolbarSide.Left, 5, MapNavigation.BuildFind));
    _registrations.Add(MapToolbar.Add(MapToolbarSide.Left, 10, MapNavigation.BuildTowns));
    _registrations.Add(MapToolbar.Add(MapToolbarSide.Left, 20, MapNavigation.BuildTrains));
    _registrations.Add(MapToolbar.Add(MapToolbarSide.Left, 30, MapNavigation.BuildFollow));
    _registrations.Add(MapToolbar.Add(MapToolbarSide.Left, 40, MapNavigation.BuildWebMap));
    _registrations.Add(MapToolbar.Add(MapToolbarSide.Right, 10, MapNavigation.BuildTrackClassLegend));
    _harmony.PatchAll(typeof(MapPlusMod).Assembly);
  }

  public void OnModUnload()
  {
    UpdateCheck.Unregister("Map Plus");
    ModsTab.RemoveAll();
    OnWorldUnload();
    WebMap.Stop();
    _harmony?.UnpatchAll(_harmony.Id);
    _harmony = null;
    foreach (var registration in _registrations) registration.Dispose();
    _registrations.Clear();
    ToolbarIcons.Reset();
  }

  public void OnWorldLoad()
  {
    UpdateCheck.WorldLoaded();
    if (_host != null) return;
    _host = new GameObject("Map Plus");
    if (!MapEnabled)
    {
      _host.AddComponent<TrackPainter>();
      return;
    }
    _host.AddComponent<MapTerrain>();
    _host.AddComponent<CarIcons>();
    _host.AddComponent<TrackPainter>();
    _host.AddComponent<MapTooltips>();
    _host.AddComponent<MapFollower>();
    _host.AddComponent<RouteOverlay>();
    _host.AddComponent<SignalIcons>();
    _host.AddComponent<FuseeIcons>();
    _host.AddComponent<PlaceIcons>();
    _host.AddComponent<OrderMarkers>();
    _host.AddComponent<ScaleBar>();
    _host.AddComponent<SelectionHalo>();
  }

  public void OnWorldUnload()
  {
    WebTerrain.WorldChanged();
    WebRivers.Reset();
    PlaceData.Reset();
    WebTrackInfo.Reset();
    WebTrain.Reset();
    if (_host == null) return;
    UnityEngine.Object.Destroy(_host);
    _host = null;
  }
}
