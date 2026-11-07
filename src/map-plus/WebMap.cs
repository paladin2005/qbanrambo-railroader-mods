using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using HTTP;
using UI.Common;
using UnityEngine;
using HttpRequest = WebSocketSharp.Net.HttpListenerRequest;
using HttpResponse = WebSocketSharp.Net.HttpListenerResponse;

namespace QbanRambo.MapPlus;

internal sealed class WebMap : MonoBehaviour
{
  internal const string Root = "/map-plus";

  private const float CheckSeconds = 2f;
  private const string JsonType = "application/json; charset=utf-8";
  private const string TerrainData = Root + "/api/terrain-data";
  private const string Train = Root + "/api/train";
  private const string Place = Root + "/api/place";
  private const string Select = Root + "/api/select";
  private const string FuseeApi = Root + "/api/fusee";

  private static readonly FieldInfo ServerField = AccessTools.Field(typeof(GameServer), "_httpServer");

  private static readonly Dictionary<string, string> Pages = new()
  {
    { Root, "index.html" }
  };

  private static readonly Dictionary<string, string> Types = new()
  {
    { ".html", "text/html; charset=utf-8" },
    { ".js", "text/javascript; charset=utf-8" },
    { ".css", "text/css; charset=utf-8" },
    { ".json", JsonType },
    { ".png", "image/png" },
    { ".svg", "image/svg+xml" }
  };

  private static readonly Dictionary<string, Func<byte[]>> Api = new()
  {
    { Root + "/api/track", WebMapData.Track },
    { Root + "/api/state", WebMapData.State },
    { Root + "/api/routes", WebMapData.Routes },
    { Root + "/api/details", WebMapData.Details },
    { Root + "/api/terrain", WebTerrain.Status },
    { Root + "/api/places", WebPlaces.Build },
    { Root + "/api/segments", WebTrackInfo.Build }
  };

  private static WebMap _shared;

  private string _directory;
  private HTTPServer _server;
  private readonly List<string> _files = new();
  private float _nextCheck;
  private bool _logged;

  internal static void Start(string modDirectory)
  {
    if (_shared != null) return;
    var host = new GameObject("Map Plus Web Map");
    DontDestroyOnLoad(host);
    _shared = host.AddComponent<WebMap>();
    _shared._directory = Path.Combine(modDirectory ?? "", "web");
    WebTerrain.Init();
  }

  internal static void Stop()
  {
    if (_shared == null) return;
    _shared.Unregister();
    Destroy(_shared.gameObject);
    _shared = null;
  }

  internal static void Open()
  {
    try
    {
      var server = GameServer.Shared;
      if (server == null || !server.IsRunning)
      {
        Toast.Present("Turn on Preferences > Features > Experimental > HTTP Server to open the web map.", ToastPosition.Bottom);
        return;
      }
      Application.OpenURL($"http://localhost:{server.Port}{Root}");
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Error(exception, "Couldn't open the web map");
    }
  }

  private void Update()
  {
    if (Time.unscaledTime < _nextCheck) return;
    _nextCheck = Time.unscaledTime + CheckSeconds;
    try
    {
      WebMapData.Tick();
      var server = GameServer.Shared != null ? ServerField?.GetValue(GameServer.Shared) as HTTPServer : null;
      if (server == _server) return;
      _server = server;
      if (server != null) Register(server);
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't connect the web map to the game's HTTP server");
    }
  }

  private void Register(HTTPServer server)
  {
    _files.Clear();
    foreach (var page in Pages)
    {
      var name = page.Value;
      server.RegisterHttpGetHandler(page.Key, (_, response) => ServeFile(response, name));
      _files.Add(page.Key);
    }
    if (Directory.Exists(_directory))
    {
      foreach (var file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
      {
        var name = file.Substring(_directory.Length).TrimStart('\\', '/').Replace('\\', '/');
        if (!Types.ContainsKey(Path.GetExtension(name).ToLowerInvariant())) continue;
        var path = Root + "/" + name;
        server.RegisterHttpGetHandler(path, (_, response) => ServeFile(response, name));
        _files.Add(path);
      }
    }
    foreach (var endpoint in Api)
    {
      var build = endpoint.Value;
      server.RegisterHttpGetHandler(endpoint.Key, (_, response) => ServeJson(response, build));
    }
    server.RegisterHttpGetHandler(TerrainData, (_, response) => ServeTerrain(response));
    server.RegisterHttpGetHandler(Train, (request, response) => ServeJson(response, () => WebTrain.Build(request.Headers[WebTrain.Header])));
    server.RegisterHttpGetHandler(Place, (request, response) => ServeJson(response, () => WebPlaces.Detail(request.Headers[WebPlaces.Header])));
    server.RegisterHttpGetHandler(Select, (request, response) => ServeJson(response, () => WebMapData.Select(request.Headers[WebTrain.Header])));
    server.RegisterHttpGetHandler(FuseeApi, (request, response) => ServeJson(response, () => WebMapData.Fusee(request.Headers["X-Map-Plus-Point"], request.Headers["X-Map-Plus-Action"], request.Headers["X-Map-Plus-Fusee"])));
    server.RegisterService(new ServiceInfo("Map Plus", "A live map of your railroad, its trains and signals, for a second monitor.", Root, SessionRequirements.GameSession));
    MapPlusMod.Log?.Info($"Web map ready at http://localhost:{server.Port}{Root}");
  }

  private void Unregister()
  {
    if (_server == null) return;
    try
    {
      foreach (var path in _files) _server.UnregisterHttpGetHandler(path);
      foreach (var path in Api.Keys) _server.UnregisterHttpGetHandler(path);
      _server.UnregisterHttpGetHandler(TerrainData);
      _server.UnregisterHttpGetHandler(Train);
      _server.UnregisterHttpGetHandler(Place);
      _server.UnregisterHttpGetHandler(Select);
      _server.UnregisterHttpGetHandler(FuseeApi);
    }
    catch (Exception exception)
    {
      MapPlusMod.Log?.Warning($"Couldn't remove the web map from the HTTP server: {exception.Message}");
    }
    _server = null;
  }

  private void ServeFile(HttpResponse response, string name)
  {
    try
    {
      var path = Path.Combine(_directory, name);
      if (!File.Exists(path))
      {
        Send(response, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes($"Map Plus is missing {name}"));
        return;
      }
      var type = Types.TryGetValue(Path.GetExtension(name).ToLowerInvariant(), out var known) ? known : "application/octet-stream";
      Send(response, 200, type, File.ReadAllBytes(path));
    }
    catch (Exception exception)
    {
      Fail(response, exception, name);
    }
  }

  private static void ServeJson(HttpResponse response, Func<byte[]> build)
  {
    try
    {
      Send(response, 200, JsonType, build());
    }
    catch (Exception exception)
    {
      Fail(response, exception, "web map data");
    }
  }

  private static void ServeTerrain(HttpResponse response)
  {
    var data = WebTerrain.Data;
    if (data == null)
    {
      Send(response, 404, JsonType, Encoding.UTF8.GetBytes("{\"ok\":false,\"reason\":\"The terrain isn't ready yet\"}"));
      return;
    }
    try
    {
      response.SetHeader("Content-Encoding", "gzip");
      Send(response, 200, "application/octet-stream", data);
    }
    catch (Exception exception)
    {
      Fail(response, exception, "the terrain");
    }
  }

  private static void Fail(HttpResponse response, Exception exception, string what)
  {
    MapPlusMod.Log?.Error(exception, $"Couldn't serve {what}");
    try
    {
      Send(response, 500, JsonType, Encoding.UTF8.GetBytes("{\"ok\":false,\"reason\":\"Map Plus error, see the Message Log\"}"));
    }
    catch (Exception)
    {
    }
  }

  private static void Send(HttpResponse response, int status, string type, byte[] body)
  {
    response.StatusCode = status;
    response.ContentType = type;
    response.SetHeader("Cache-Control", "no-store");
    response.ContentLength64 = body.Length;
    response.OutputStream.Write(body, 0, body.Length);
    response.Close();
  }
}
