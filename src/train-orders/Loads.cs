using System.Linq;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace QbanRambo.TrainOrders;

internal readonly struct LoadState
{
  public LoadState(int full, int empty, int total, float fill)
  {
    Full = full;
    Empty = empty;
    Total = total;
    Fill = fill;
  }

  public int Full { get; }
  public int Empty { get; }
  public int Total { get; }
  public float Fill { get; }
  public bool AllFull => Full >= Total;
  public bool AllEmpty => Empty >= Total;
}

internal static class Loads
{
  private const float FullShare = 0.99f;
  private const float EmptyShare = 0.01f;

  internal static LoadState Measure(Car locomotive)
  {
    var full = 0;
    var empty = 0;
    var total = 0;
    var fill = 0f;
    foreach (var car in locomotive.EnumerateCoupled().Where(IsLoadable))
    {
      var share = FillShare(car);
      var pending = AtDestination(car);
      total++;
      fill += share;
      if (share >= FullShare && !pending) full++;
      if (share <= EmptyShare && !pending) empty++;
    }
    return new LoadState(full, empty, total, fill);
  }

  private static bool AtDestination(Car car)
  {
    var ops = OpsController.Shared;
    return ops != null && ops.TryGetDestinationInfo(car, out _, out var atDestination, out _, out _) && atDestination;
  }

  private static bool IsLoadable(Car car) => car.CarType.IsFreight && car.Definition?.LoadSlots is { Count: > 0 };

  private static float FillShare(Car car)
  {
    var slots = car.Definition.LoadSlots;
    var best = 0f;
    for (var index = 0; index < slots.Count; index++)
    {
      var info = car.GetLoadInfo(index);
      if (!info.HasValue || string.IsNullOrEmpty(info.Value.LoadId) || info.Value.Quantity <= 0f) continue;
      var capacity = slots[index].CalculateMaximumCapacity(info.Value.LoadId, car);
      if (capacity > 0f) best = Mathf.Max(best, Mathf.Clamp01(info.Value.Quantity / capacity));
    }
    return best;
  }
}
