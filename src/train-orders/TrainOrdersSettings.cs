using Game;
using UI.Builder;

namespace QbanRambo.TrainOrders;

internal static class TrainOrdersSettings
{
  private const string OneWindowKey = "qbanrambo.train-orders.one-window";
  private const string CoupleAirKey = "qbanrambo.train-orders.couple-air";
  private const string RerouteKey = "qbanrambo.train-orders.reroute";
  private const string AutoStartKey = "qbanrambo.train-orders.auto-start";

  private static readonly UIState<bool> Expanded = new(true);
  private static bool? _oneWindow;
  private static bool? _coupleAir;
  private static bool? _reroute;
  private static bool? _autoStart;

  internal static bool AutoStart
  {
    get => _autoStart ??= GlobalPreferences.GetInt(AutoStartKey, 0) != 0;
    set
    {
      _autoStart = value;
      GlobalPreferences.SetInt(AutoStartKey, value ? 1 : 0);
      GlobalPreferences.Flush();
    }
  }

  internal static bool Reroute
  {
    get => _reroute ??= GlobalPreferences.GetInt(RerouteKey, 1) != 0;
    set
    {
      _reroute = value;
      GlobalPreferences.SetInt(RerouteKey, value ? 1 : 0);
      GlobalPreferences.Flush();
    }
  }

  internal static bool CoupleAir
  {
    get => _coupleAir ??= GlobalPreferences.GetInt(CoupleAirKey, 1) != 0;
    set
    {
      _coupleAir = value;
      GlobalPreferences.SetInt(CoupleAirKey, value ? 1 : 0);
      GlobalPreferences.Flush();
    }
  }

  internal static bool OneWindow
  {
    get => _oneWindow ??= GlobalPreferences.GetInt(OneWindowKey, 0) != 0;
    set
    {
      _oneWindow = value;
      GlobalPreferences.SetInt(OneWindowKey, value ? 1 : 0);
      GlobalPreferences.Flush();
    }
  }

  internal static void Draw(UIPanelBuilder builder)
  {
    ModsTab.Section(builder, "Train Orders", Expanded, section =>
    {
      ModsTab.Toggle(section, "Air and handbrakes after coupling", () => CoupleAir, on => CoupleAir = on, "On: when you send a locomotive to couple to a car (the game's waypoint tool on a car) without its Train Orders window open, the air is connected and the handbrakes released once it's coupled and stopped, as Train Orders does with its own couple orders. Only for the orders you give.");
      ModsTab.Toggle(section, "Take a shorter way when one opens", () => Reroute, on => Reroute = on, "On: while a train runs its orders, every 30 seconds the engineer checks for a way to the waypoint that's clearly shorter than the one it's on (say the train that was in the way when it set off has gone) and takes it. The game's own reroute, Shift+click on the waypoint button, does it once.");
      ModsTab.Toggle(section, "Start with the first order", () => AutoStart, on => AutoStart = on, "On: adding an order to an empty list starts the orders, so there's no need to press Start. Off: press Start yourself.");
      ModsTab.Toggle(section, "One window for the selected train", () => OneWindow, on => OneWindow = on, "On: one Train Orders window shows the orders of the locomotive you select, and Train Orders on another locomotive switches it. Off: every locomotive gets its own window. Orders keep running either way.");
    });
  }
}
