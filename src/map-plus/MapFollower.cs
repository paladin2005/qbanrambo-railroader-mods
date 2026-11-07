using System;
using UI.Map;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal sealed class MapFollower : MonoBehaviour
{
  private const float HookSeconds = 1f;

  private MapDrag _drag;
  private Action _original;
  private Action _hook;
  private float _nextHook;
  private bool _logged;

  private void LateUpdate()
  {
    try
    {
      HookDrag();
      if (!MapNavigation.Following) return;
      var target = MapNavigation.FollowTarget;
      if (target == null)
      {
        MapNavigation.Following = false;
        return;
      }
      var builder = MapBuilder.Shared;
      if (builder == null || builder.mapCamera == null || !builder.mapCamera.gameObject.activeInHierarchy) return;
      builder.SetMapCenter(target.GetCenterPosition());
    }
    catch (Exception exception)
    {
      MapNavigation.Following = false;
      if (_logged) return;
      _logged = true;
      MapPlusMod.Log?.Error(exception, "Couldn't follow the train");
    }
  }

  private void OnDestroy()
  {
    if (_drag != null && _hook != null && _drag.OnDragStart == _hook) _drag.OnDragStart = _original;
    MapNavigation.Reset();
  }

  private void HookDrag()
  {
    if (Time.unscaledTime < _nextHook) return;
    _nextHook = Time.unscaledTime + HookSeconds;
    if (_drag == null) _drag = FindAnyObjectByType<MapDrag>(FindObjectsInactive.Include);
    if (_drag == null || (_hook != null && _drag.OnDragStart == _hook)) return;
    var original = _drag.OnDragStart;
    _original = original;
    _hook = () =>
    {
      original?.Invoke();
      MapNavigation.Following = false;
    };
    _drag.OnDragStart = _hook;
  }
}
