using System;
using System.IO;
using System.Linq;
using Game;
using UI.Builder;

namespace QbanRambo.MapPlus;

internal static class MapPlusConfig
{
  private const string FileName = "map-plus.cfg";
  private const string InGameKey = "EnableMapInGame";

  internal static string FilePath { get; private set; }

  internal static bool MapInGame { get; private set; } = true;

  internal static bool MapInGameSaved { get; private set; } = true;

  private static readonly UIState<bool> Expanded = new(true);

  internal static void Draw(UIPanelBuilder builder)
  {
    ModsTab.Section(builder, "Map Plus", Expanded, section =>
    {
      ModsTab.Toggle(section, "On the game's map", () => MapInGameSaved, SetMapInGame, $"On: Map Plus adds its layers, buttons and tools to the game's map. Off: the game's map is left alone and only the web map runs (http://localhost:8630/map-plus). Saved in {FileName}; takes effect the next time Railroader starts.");
      section.AddLabel(() => MapInGameSaved != MapPlusMod.MapEnabled ? ModsTab.Note("Restart Railroader to apply this.") : "", UIPanelBuilder.Frequency.Periodic);
    });
  }

  private static void SetMapInGame(bool on)
  {
    if (FilePath == null) return;
    var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath).ToList() : FreshConfig().ToList();
    var index = lines.FindLastIndex(IsSwitchLine);
    var line = $"{InGameKey}={(on ? 1 : 0)}";
    if (index >= 0) lines[index] = line;
    else lines.Add(line);
    File.WriteAllLines(FilePath, lines);
    MapInGameSaved = on;
  }

  private static bool IsSwitchLine(string raw)
  {
    var line = raw.Trim();
    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) return false;
    var equals = line.IndexOf('=');
    return equals > 0 && line.Substring(0, equals).Trim().Equals(InGameKey, StringComparison.OrdinalIgnoreCase);
  }

  private static string[] FreshConfig() => new[]
  {
    "# Map Plus settings, also in Preferences > Mods. Restart Railroader after changing them.",
    "",
    "# 1: Map Plus adds its layers, buttons and tools to the game's map.",
    "# 0: the game's map is left alone and only the web map runs (http://localhost:8630/map-plus).",
    $"{InGameKey}=1"
  };

  internal static void Load()
  {
    MapInGame = true;
    try
    {
      FilePath = Path.Combine(UserPaths.RailroaderDirectory, FileName);
      if (!File.Exists(FilePath))
      {
        Write();
        return;
      }
      foreach (var raw in File.ReadAllLines(FilePath))
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
        var equals = line.IndexOf('=');
        if (equals <= 0) continue;
        if (!line.Substring(0, equals).Trim().Equals(InGameKey, StringComparison.OrdinalIgnoreCase)) continue;
        MapInGame = !IsOff(line.Substring(equals + 1).Trim());
      }
      MapInGameSaved = MapInGame;
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Warning($"Couldn't read {FileName}: {exception.Message}");
    }
  }

  private static bool IsOff(string value)
  {
    return value == "0"
      || value.Equals("false", StringComparison.OrdinalIgnoreCase)
      || value.Equals("off", StringComparison.OrdinalIgnoreCase)
      || value.Equals("no", StringComparison.OrdinalIgnoreCase);
  }

  private static void Write()
  {
    try
    {
      File.WriteAllLines(FilePath, FreshConfig());
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Warning($"Couldn't create {FileName}: {exception.Message}");
    }
  }
}
