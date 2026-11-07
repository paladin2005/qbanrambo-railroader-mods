using System.Collections.Generic;
using System.Linq;
using Model;

namespace QbanRambo.MapPlus;

internal static class Consist
{
  internal static IEnumerable<Car> Of(Car car) => car == null || car.set == null ? Enumerable.Empty<Car>() : car.EnumerateCoupled();
}
