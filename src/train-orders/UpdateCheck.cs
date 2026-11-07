using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using UI.Builder;
using UnityEngine;
using UnityEngine.Networking;

namespace QbanRambo.TrainOrders;

internal static class UpdateCheck
{
  private const string RegistryKey = "qbanrambo.updates";
  private const string ConfigName = "QbanRamboGlobal.cfg";
  private const string SwitchKey = "UpdateCheck";
  private const string NotifiedPrefix = "qbanrambo.updates.notified.";
  private const string Endpoint = "https://api.nexusmods.com/v2/graphql";
  private const int GameId = 5982;
  private const int TimeoutSeconds = 15;

  private static IModLogger _log;
  private static bool _owner;
  private static bool _started;
  private static bool _warned;
  private static bool? _enabled;
  private static readonly UIState<bool> Expanded = new(true);

  internal static bool IsOwner => _owner;

  internal static void DrawSection(UIPanelBuilder builder)
  {
    ModsTab.Section(builder, "QbanRambo's mods", Expanded, section =>
    {
      ModsTab.Toggle(section, "Check Nexus for updates", Enabled, SetEnabled, "Once a session, when a game loads, ask Nexus for the newest versions of Goto, Map Plus and Train Orders, and say once in the console when one is out. Nothing about you is sent. Saved in QbanRamboGlobal.cfg; takes effect the next time Railroader starts.");
    });
  }

  internal static void Register(string name, int nexusId, IModLogger log)
  {
    try
    {
      var version = typeof(UpdateCheck).Assembly.GetName().Version;
      var installed = $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
      if (AppDomain.CurrentDomain.GetData(RegistryKey) is not Dictionary<string, string> registry)
      {
        registry = new Dictionary<string, string>();
        AppDomain.CurrentDomain.SetData(RegistryKey, registry);
        _owner = true;
        _log = log;
      }
      registry[name] = $"{nexusId}|{installed}";
    }
    catch (Exception exception)
    {
      log?.Warning($"Couldn't register for the update check: {exception.Message}");
    }
  }

  internal static void Unregister(string name)
  {
    if (AppDomain.CurrentDomain.GetData(RegistryKey) is Dictionary<string, string> registry) registry.Remove(name);
    _owner = false;
  }

  internal static void WorldLoaded()
  {
    if (!_owner || _started) return;
    _started = true;
    try
    {
      if (!Enabled())
      {
        _log?.Info($"Update check is off (Preferences > Mods, or {ConfigName})");
        return;
      }
      var host = new GameObject("QbanRambo Update Check");
      UnityEngine.Object.DontDestroyOnLoad(host);
      host.AddComponent<UpdateCheckRunner>();
    }
    catch (Exception exception)
    {
      Quiet(exception.Message);
    }
  }

  internal static UnityWebRequest CreateRequest()
  {
    var mods = Registered();
    if (mods.Count == 0) return null;
    var fields = string.Join(" ", mods.Select(mod => $"m{mod.NexusId}: modFiles(modId: {mod.NexusId}, gameId: {GameId}) {{ version category }}"));
    var body = new JObject { ["query"] = $"{{ {fields} }}" }.ToString(Newtonsoft.Json.Formatting.None);
    var request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST)
    {
      uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
      downloadHandler = new DownloadHandlerBuffer(),
      timeout = TimeoutSeconds
    };
    request.SetRequestHeader("Content-Type", "application/json");
    try
    {
      request.SetRequestHeader("User-Agent", "QbanRambo-Mods");
    }
    catch (Exception)
    {
    }
    return request;
  }

  internal static void Handle(UnityWebRequest request)
  {
    if (request.result != UnityWebRequest.Result.Success)
    {
      Quiet(request.error);
      return;
    }
    var data = JObject.Parse(request.downloadHandler.text)["data"] as JObject;
    if (data == null)
    {
      Quiet("no data in the answer");
      return;
    }
    var newer = new List<(string Name, string Latest, string Installed)>();
    foreach (var mod in Registered())
    {
      if (data[$"m{mod.NexusId}"] is not JArray files) continue;
      var latest = Latest(files);
      if (latest == null || !TryVersion(mod.Installed, out var installed) || latest <= installed) continue;
      newer.Add((mod.Name, Text(latest), mod.Installed));
    }
    if (newer.Count == 0)
    {
      _log?.Info("Update check: QbanRambo's mods are up to date");
      return;
    }
    var list = string.Join(", ", newer.Select(mod => $"{mod.Name} {mod.Latest}"));
    _log?.Info($"New versions on Nexus: {string.Join(", ", newer.Select(mod => $"{mod.Name} {mod.Latest} (you have {mod.Installed})"))}");
    var fresh = newer.Where(mod => GlobalPreferences.GetString(NotifiedPrefix + mod.Name, "") != mod.Latest).ToList();
    if (fresh.Count == 0) return;
    foreach (var mod in fresh) GlobalPreferences.SetString(NotifiedPrefix + mod.Name, mod.Latest);
    GlobalPreferences.Flush();
    global::Console.Log($"New versions on Nexus: {list}. The Mods menu has the links.");
  }

  internal static void Quiet(string reason)
  {
    if (_warned) return;
    _warned = true;
    _log?.Info($"Update check skipped: {reason}");
  }

  private static Version Latest(JArray files)
  {
    var versions = files.OfType<JObject>()
      .Select(file => (Category: file.Value<string>("category"), Ok: TryVersion(file.Value<string>("version"), out var version), Version: version))
      .Where(file => file.Ok)
      .ToList();
    var main = versions.Where(file => file.Category == "MAIN").Select(file => file.Version).ToList();
    if (main.Count > 0) return main.Max();
    var any = versions.Where(file => file.Category != "ARCHIVED").Select(file => file.Version).ToList();
    return any.Count > 0 ? any.Max() : null;
  }

  private static bool TryVersion(string text, out Version version)
  {
    version = null;
    if (string.IsNullOrWhiteSpace(text)) return false;
    var cleaned = text.Trim().TrimStart('v', 'V');
    if (!Version.TryParse(cleaned, out var parsed)) return false;
    version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
    return true;
  }

  private static string Text(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

  private static List<(string Name, int NexusId, string Installed)> Registered()
  {
    var mods = new List<(string, int, string)>();
    if (AppDomain.CurrentDomain.GetData(RegistryKey) is not Dictionary<string, string> registry) return mods;
    foreach (var entry in registry.ToList())
    {
      var parts = entry.Value.Split('|');
      if (parts.Length == 2 && int.TryParse(parts[0], out var id)) mods.Add((entry.Key, id, parts[1]));
    }
    return mods;
  }

  private static bool Enabled()
  {
    if (_enabled.HasValue) return _enabled.Value;
    try
    {
      var path = ConfigPath();
      if (!File.Exists(path)) File.WriteAllLines(path, FreshConfig());
      var line = File.ReadAllLines(path).LastOrDefault(IsSwitchLine);
      var value = line == null ? "1" : line.Substring(line.IndexOf('=') + 1).Trim();
      _enabled = !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase));
    }
    catch (Exception exception)
    {
      _log?.Warning($"Couldn't read {ConfigName}, so the update check stays on: {exception.Message}");
      _enabled = true;
    }
    return _enabled.Value;
  }

  private static void SetEnabled(bool on)
  {
    var path = ConfigPath();
    var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : FreshConfig().ToList();
    var index = lines.FindLastIndex(IsSwitchLine);
    var line = $"{SwitchKey}={(on ? 1 : 0)}";
    if (index >= 0) lines[index] = line;
    else lines.Add(line);
    File.WriteAllLines(path, lines);
    _enabled = on;
  }

  private static string ConfigPath() => Path.Combine(UserPaths.RailroaderDirectory, ConfigName);

  private static string[] FreshConfig() => new[]
  {
    "# Settings shared by QbanRambo's mods (Goto, Map Plus, Train Orders), also in Preferences > Mods. Restart Railroader after changing them here.",
    "",
    "# 1: when a game loads, check Nexus for newer versions of the installed mods and say so once per new version.",
    "# 0: never check (no connection to Nexus).",
    $"{SwitchKey}=1"
  };

  private static bool IsSwitchLine(string raw)
  {
    var line = raw.Trim();
    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) return false;
    var equals = line.IndexOf('=');
    return equals > 0 && line.Substring(0, equals).Trim().Equals(SwitchKey, StringComparison.OrdinalIgnoreCase);
  }
}

internal sealed class UpdateCheckRunner : MonoBehaviour
{
  private const float StartDelay = 10f;

  private UnityWebRequest _request;
  private float _startAt;

  private void Start()
  {
    _startAt = Time.realtimeSinceStartup + StartDelay;
  }

  private void Update()
  {
    try
    {
      if (_request == null)
      {
        if (Time.realtimeSinceStartup < _startAt) return;
        _request = UpdateCheck.CreateRequest();
        if (_request == null)
        {
          Destroy(gameObject);
          return;
        }
        _request.SendWebRequest();
        return;
      }
      if (!_request.isDone) return;
      UpdateCheck.Handle(_request);
      Destroy(gameObject);
    }
    catch (Exception exception)
    {
      UpdateCheck.Quiet(exception.Message);
      Destroy(gameObject);
    }
  }

  private void OnDestroy()
  {
    _request?.Dispose();
    _request = null;
  }
}
