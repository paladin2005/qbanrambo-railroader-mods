using Game;
using UI.Builder;

namespace QbanRambo.TrainOrders;

internal static class TrainOrdersSettings
{
  private const string OneWindowKey = "qbanrambo.train-orders.one-window";
  private const string CoupleAirKey = "qbanrambo.train-orders.couple-air";

  private static readonly UIState<bool> Expanded = new(true);
  private static bool? _oneWindow;
  private static bool? _coupleAir;

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
      ModsTab.Toggle(section, "One window for the selected train", () => OneWindow, on => OneWindow = on, "On: one Train Orders window shows the orders of the locomotive you select, and Train Orders on another locomotive switches it. Off: every locomotive gets its own window. Orders keep running either way.");
    });
  }
}
