using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Ops;

namespace QbanRambo.TrainOrders;

internal static class Repairs
{
  private const float Tolerance = 0.005f;

  internal static List<Car> Cars(Car locomotive)
  {
    var consist = locomotive.EnumerateCoupled().Where(car => car != null).ToList();
    var cars = consist.Where(car => car is not BaseLocomotive && !car.CarType.IsTender).ToList();
    return cars.Count > 0 ? cars : consist;
  }

  internal static bool IsRepaired(Car car) => car.Condition >= car.RepairCap - Tolerance;

  internal static (int Repaired, int Total, float Sum) Measure(Car locomotive)
  {
    var cars = Cars(locomotive);
    return (cars.Count(IsRepaired), cars.Count, cars.Sum(car => car.Condition));
  }

  internal static string Progress(Car locomotive)
  {
    var (repaired, total, _) = Measure(locomotive);
    return $"{repaired} of {total} repaired";
  }

  internal static bool OnRepairTrack(Car locomotive)
  {
    var ops = OpsController.Shared;
    var controller = TrainController.Shared;
    if (ops == null || controller == null) return false;
    var consist = new HashSet<Car>(locomotive.EnumerateCoupled());
    var buffer = new List<Car>();
    foreach (var track in ops.Areas.SelectMany(area => area.Industries).SelectMany(industry => industry.Components).OfType<RepairTrack>())
    {
      foreach (var span in track.TrackSpans)
      {
        if (span == null) continue;
        buffer.Clear();
        controller.GetCarsOnSpan(span, buffer);
        if (buffer.Any(consist.Contains)) return true;
      }
    }
    return false;
  }
}
