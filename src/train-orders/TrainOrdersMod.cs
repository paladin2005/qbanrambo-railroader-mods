using HarmonyLib;
using Railroader.Modding;
using UnityEngine;

namespace QbanRambo.TrainOrders;

public sealed class TrainOrdersMod : IModEntryPoint, IWorldLifecycle
{
  internal static IModLogger Log;

  private Harmony _harmony;
  private GameObject _host;

  public void OnModLoad(IModContext context)
  {
    Log = context.Log;
    _harmony = new Harmony(context.ModIdentifier);
    _harmony.PatchAll(typeof(TrainOrdersMod).Assembly);
    System.AppDomain.CurrentDomain.SetData(OrderCapture.TurntableHookKey, OrderCapture.TurntableHook);
    System.AppDomain.CurrentDomain.SetData(OrderCapture.RunAroundHookKey, OrderCapture.RunAroundHook);
    System.AppDomain.CurrentDomain.SetData(OrderCapture.WindowOpenKey, OrderCapture.WindowOpenHook);
    System.AppDomain.CurrentDomain.SetData(Forecast.FutureCarsKey, Forecast.FutureCarsHook);
    ModsTab.Add(_harmony, TrainOrdersSettings.Draw, context.Log);
    UpdateCheck.Register("Train Orders", 1901, context.Log);
    if (UpdateCheck.IsOwner) ModsTab.Add(_harmony, UpdateCheck.DrawSection, context.Log);
    Log.Info("Train Orders ready");
  }

  public void OnModUnload()
  {
    UpdateCheck.Unregister("Train Orders");
    ModsTab.RemoveAll();
    OnWorldUnload();
    System.AppDomain.CurrentDomain.SetData(OrderCapture.TurntableHookKey, null);
    System.AppDomain.CurrentDomain.SetData(OrderCapture.RunAroundHookKey, null);
    System.AppDomain.CurrentDomain.SetData(OrderCapture.WindowOpenKey, null);
    System.AppDomain.CurrentDomain.SetData(Forecast.FutureCarsKey, null);
    Icons.Reset();
    Silhouettes.Reset();
    _harmony?.UnpatchAll(_harmony.Id);
    _harmony = null;
  }

  public void OnWorldLoad()
  {
    UpdateCheck.WorldLoaded();
    if (_host != null) return;
    _host = new GameObject("Train Orders");
    _host.AddComponent<Dispatcher>();
    _host.AddComponent<GhostKeeper>();
  }

  public void OnWorldUnload()
  {
    OrderCapture.Reset();
    PickerForecast.Reset();
    Places.Reset();
    Fuel.Reset();
    Signals.Reset();
    StationTracks.Reset();
    OrderActions.ResetCopy();
    OrdersMenu.PendingCar = null;
    OrdersWindow.CloseAll();
    if (_host == null) return;
    Object.Destroy(_host);
    _host = null;
  }
}
