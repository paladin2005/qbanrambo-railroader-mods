using Game;
using UI.Builder;

namespace QbanRambo.Goto;

internal static class GotoSettings
{
  private const string ReleaseHandbrakesKey = "qbanrambo.goto.releaseHandbrakes";

  private static readonly UIState<bool> Expanded = new(true);

  internal static bool ReleaseHandbrakes
  {
    get => GlobalPreferences.GetInt(ReleaseHandbrakesKey, 1) != 0;
    set
    {
      GlobalPreferences.SetInt(ReleaseHandbrakesKey, value ? 1 : 0);
      GlobalPreferences.Flush();
    }
  }

  internal static void Draw(UIPanelBuilder builder)
  {
    ModsTab.Section(builder, "QbanRambo's Goto", Expanded, section =>
    {
      ModsTab.Toggle(section, "Release handbrakes", () => ReleaseHandbrakes, on => ReleaseHandbrakes = on, "When Go to sends a train, release every handbrake in it and say how many in the message. Also under Go to in the locomotive's menu.");
    });
  }
}
