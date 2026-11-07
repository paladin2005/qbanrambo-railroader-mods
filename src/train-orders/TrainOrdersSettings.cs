using Game;
using UI.Builder;

namespace QbanRambo.TrainOrders;

internal static class TrainOrdersSettings
{
  private const string OneWindowKey = "qbanrambo.train-orders.one-window";

  private static readonly UIState<bool> Expanded = new(true);
  private static bool? _oneWindow;

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
      ModsTab.Toggle(section, "One window for the selected train", () => OneWindow, on => OneWindow = on, "On: one Train Orders window shows the orders of the locomotive you select, and Train Orders on another locomotive switches it. Off: every locomotive gets its own window. Orders keep running either way.");
    });
  }
}
