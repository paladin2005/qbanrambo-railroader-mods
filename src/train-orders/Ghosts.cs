using System;
using System.Collections.Generic;
using System.Linq;
using Track;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QbanRambo.TrainOrders;

internal static class Ghosts
{
  private static readonly Color GhostColor = new(0.25f, 0.85f, 1f);
  private static readonly List<string> Tokens = new();
  private static OrdersWindow _owner;
  private static GhostState _shown;
  private static bool _logged;

  internal static void Show(OrdersWindow owner, GhostState state)
  {
    if (owner == _owner && (ReferenceEquals(state, _shown) || (state != null && _shown != null && state.Signature == _shown.Signature)))
    {
      _shown = state;
      return;
    }
    Remove();
    _owner = owner;
    _shown = state;
    if (state == null) return;
    try
    {
      var indicators = SegmentIndicatorController.Shared;
      if (indicators == null) return;
      foreach (var (car, spot) in state.Left) Tokens.Add(indicators.Add(spot.Path, GhostColor, ShowsChevrons(car)));
      foreach (var (car, spot) in state.Train) Tokens.Add(indicators.Add(spot.Reversed().Path, GhostColor, ShowsChevrons(car)));
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      TrainOrdersMod.Log?.Error(exception, "Couldn't draw the train forecast on the track");
    }
  }

  internal static void ShowTrack(OrdersWindow owner, IReadOnlyList<TrackSegment> segments)
  {
    Remove();
    _owner = owner;
    _shown = null;
    if (segments == null || segments.Count == 0) return;
    try
    {
      var indicators = SegmentIndicatorController.Shared;
      if (indicators != null) Tokens.Add(indicators.Add(segments));
    }
    catch (Exception exception)
    {
      if (_logged) return;
      _logged = true;
      TrainOrdersMod.Log?.Error(exception, "Couldn't light the turntable track");
    }
  }

  internal static IReadOnlyList<TrackSegment> TurntableTrack(OrderStep step)
  {
    if (step == null || step.Kind != StepKind.Turntable) return null;
    var site = Turntables.Find(step.TurntableId);
    if (site == null) return null;
    if (!step.Exit.HasValue) return site.Turntable.BridgeSegment != null ? new[] { site.Turntable.BridgeSegment } : null;
    return site.Tracks.FirstOrDefault(track => track.Index == step.Exit.Value)?.Segments.ToList();
  }

  private static bool ShowsChevrons(ForecastCar car) => car.Car != null && (car.Car.CarType.IsLocomotive || car.Car.CarType.IsTender);

  internal static void Clear(OrdersWindow owner)
  {
    if (owner != _owner) return;
    Remove();
    _owner = null;
    _shown = null;
  }

  internal static void Reset()
  {
    Remove();
    _owner = null;
    _shown = null;
  }

  internal static void Tick()
  {
    if (_owner == null) return;
    if (!_owner || !_owner.IsOpen) Reset();
  }

  private static void Remove()
  {
    var indicators = SegmentIndicatorController.Shared;
    if (indicators != null)
    {
      foreach (var token in Tokens)
      {
        try
        {
          indicators.Remove(token);
        }
        catch (Exception)
        {
        }
      }
    }
    Tokens.Clear();
  }
}

internal sealed class GhostKeeper : MonoBehaviour
{
  private void Update() => Ghosts.Tick();

  private void OnDestroy() => Ghosts.Reset();
}

internal sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
  public Action Enter { get; set; }
  public Action Exit { get; set; }

  public void OnPointerEnter(PointerEventData eventData) => Run(Enter);

  public void OnPointerExit(PointerEventData eventData) => Run(Exit);

  private static void Run(Action action)
  {
    try
    {
      action?.Invoke();
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't show the order's forecast");
    }
  }
}
