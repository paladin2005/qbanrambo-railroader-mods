using Helpers;
using Map.Runtime;
using Track;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class MapMouse
{
  private static MapDrag _drag;

  internal static MapDrag Drag
  {
    get
    {
      if (_drag == null) _drag = Object.FindAnyObjectByType<MapDrag>(FindObjectsInactive.Include);
      return _drag;
    }
  }

  internal static bool IsMapShown(out Camera camera)
  {
    var builder = MapBuilder.Shared;
    camera = builder != null ? builder.mapCamera : null;
    return camera != null && camera.gameObject.activeInHierarchy;
  }

  internal static float MetersPerUnit(Camera camera)
  {
    var drag = Drag;
    var height = drag != null ? ((RectTransform)drag.transform).rect.height : 0f;
    return height > 1f ? 2f * camera.orthographicSize / height : 2f * camera.orthographicSize / 500f;
  }

  internal static bool TryGetRay(MapDrag drag, out Ray ray, out float metersPerUnit)
  {
    ray = default;
    metersPerUnit = 0f;
    var builder = MapBuilder.Shared;
    if (drag == null || builder == null || builder.mapCamera == null) return false;
    var rect = (RectTransform)drag.transform;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, Input.mousePosition, null, out var local)) return false;
    var bounds = rect.rect;
    if (bounds.width <= 0f || bounds.height <= 0f) return false;
    var viewport = new Vector3((local.x - bounds.x) / bounds.width, (local.y - bounds.y) / bounds.height, 0f);
    var camera = builder.mapCamera;
    ray = camera.ViewportPointToRay(viewport);
    metersPerUnit = 2f * camera.orthographicSize / bounds.height;
    return true;
  }

  internal static bool TryGetTrack(Ray ray, float radius, out Location location)
  {
    location = default;
    var graph = Graph.Shared;
    var terrain = MapManager.Instance;
    if (graph == null || terrain == null) return false;
    if (!terrain.TryFindTerrainPointForXZ(WorldTransformer.WorldToGame(ray.origin), out var point)) return false;
    return graph.TryGetLocationFromGamePoint(point, radius, out location);
  }
}
