using HarmonyLib;
using Railroader.Modding;

namespace QbanRambo.Goto;

public sealed class GotoMod : IModEntryPoint, IWorldLifecycle
{
  private Harmony _harmony;

  public void OnModLoad(IModContext context)
  {
    GotoMenu.Log = context.Log;
    GotoMenu.PendingCar = null;
    _harmony = new Harmony(context.ModIdentifier);
    _harmony.PatchAll(typeof(GotoMod).Assembly);
    System.AppDomain.CurrentDomain.SetData(GotoMenu.MenuHookKey, GotoMenu.MenuHook);
    ModsTab.Add(_harmony, GotoSettings.Draw, context.Log);
    UpdateCheck.Register("Goto", 1887, context.Log);
    if (UpdateCheck.IsOwner) ModsTab.Add(_harmony, UpdateCheck.DrawSection, context.Log);
    context.Log.Info("Go to menu ready");
  }

  public void OnModUnload()
  {
    ModsTab.RemoveAll();
    _harmony?.UnpatchAll(_harmony.Id);
    _harmony = null;
    UpdateCheck.Unregister("Goto");
    PanelButton.RemoveAll();
    System.AppDomain.CurrentDomain.SetData(GotoMenu.MenuHookKey, null);
    GotoMenu.PendingCar = null;
    StationWatch.Clear();
    RefuelWatch.Clear();
    TurntableWatch.Clear();
    CoupleWatch.Clear();
    DeliverWatch.Clear();
    RunAroundWatch.Clear();
  }

  public void OnWorldLoad()
  {
    UpdateCheck.WorldLoaded();
    PanelButton.EnsureAll();
    RefuelWatch.ResumeSoon();
    TurntableWatch.ResumeSoon();
    StationWatch.ResumeSoon();
  }

  public void OnWorldUnload()
  {
    RefuelWatch.Drop();
    TurntableWatch.Clear();
    StationWatch.Clear();
    CoupleWatch.Clear();
    DeliverWatch.Clear();
    RunAroundWatch.Clear();
  }
}
