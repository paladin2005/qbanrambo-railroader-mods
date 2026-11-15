using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Game;
using Game.State;
using HarmonyLib;
using Model;
using Model.Ops.Timetable;
using TimetableTrain = Model.Ops.Timetable.Timetable.Train;

namespace QbanRambo.TrainOrders;

internal static class TimetableTrains
{
  private const int MinutesPerDay = 24 * 60;

  private static MethodInfo _label;
  private static bool _labelLooked;

  internal static IReadOnlyList<TimetableTrain> All()
  {
    var trains = TimetableController.Shared?.Current?.Trains;
    if (trains == null) return Array.Empty<TimetableTrain>();
    return trains.Values.Where(train => train != null).OrderBy(train => train.SortName).ToList();
  }

  internal static TimetableTrain Find(string symbol)
  {
    if (string.IsNullOrEmpty(symbol)) return null;
    var controller = TimetableController.Shared;
    return controller != null && controller.TryGetTrainForSymbol(symbol, out var train) ? train : null;
  }

  internal static string Short(string symbol) => Find(symbol)?.DisplayStringShort ?? symbol;

  internal static string Label(TimetableTrain train)
  {
    try
    {
      if (!_labelLooked)
      {
        _labelLooked = true;
        _label = AccessTools.Method(typeof(UI.CompanyWindow.CrewsPanelBuilder), "DropdownLabelForTimetableTrain", new[] { typeof(TimetableTrain) });
      }
      if (_label?.Invoke(null, new object[] { train }) is string text && !string.IsNullOrEmpty(text)) return text;
    }
    catch (Exception)
    {
    }
    return train.DisplayStringLong;
  }

  internal static string Plain(string text) => Regex.Replace(text ?? "", "<[^>]+>", "");

  internal static bool HasCrew(Car locomotive) => !string.IsNullOrEmpty(locomotive.trainCrewId) && StateManager.Shared?.PlayersManager != null && StateManager.Shared.PlayersManager.TrainCrewForId(locomotive.trainCrewId, out _);

  internal static TimetableTrain Current(Car locomotive)
  {
    var controller = TimetableController.Shared;
    if (controller == null || string.IsNullOrEmpty(locomotive?.trainCrewId)) return null;
    return controller.TryGetTrainForTrainCrewId(locomotive.trainCrewId, out var train) ? train : null;
  }

  internal static TimetableTrain Next(Car locomotive)
  {
    var current = Current(locomotive);
    var trains = All().Where(train => train.Entries.Count > 0 && (current == null || train.TrainType == current.TrainType)).ToList();
    if (trains.Count == 0) return null;
    if (current == null || current.Entries.Count == 0)
    {
      var now = OrderStep.Minute(TimeWeather.Now) % MinutesPerDay;
      return trains.Where(train => Start(train) >= now).OrderBy(Start).FirstOrDefault() ?? trains.OrderBy(Start).First();
    }
    var others = trains.Where(train => train.Name != current.Name).ToList();
    var end = current.Entries[current.Entries.Count - 1].Station;
    var connecting = others.Where(train => train.Entries[0].Station == end).ToList();
    if (connecting.Count > 0)
    {
      var arrival = End(current);
      return connecting.Where(train => Start(train) >= arrival).OrderBy(Start).FirstOrDefault() ?? connecting.OrderBy(Start).First();
    }
    var start = Start(current);
    return others.Where(train => Start(train) > start).OrderBy(Start).FirstOrDefault() ?? (others.Count > 0 ? others : trains).OrderBy(Start).First();
  }

  internal static bool StopsAt(TimetableTrain train, string stationId)
  {
    var stop = Passengers.Stop(stationId);
    if (stop == null) return true;
    return !string.IsNullOrEmpty(stop.timetableCode) && train.Entries.Any(entry => entry.Station == stop.timetableCode);
  }

  internal static TimetableTrain At(Car locomotive, OrderList orders, int index, out bool known)
  {
    known = true;
    for (var i = index - 1; i >= 0; i--)
    {
      var step = orders.Steps[i];
      if (step.Kind != StepKind.Timetable) continue;
      if (step.Count == OrderStep.TimetableNext)
      {
        known = false;
        return null;
      }
      return step.Count == OrderStep.TimetableNone ? null : Find(step.Symbol);
    }
    if (orders.Repeat && orders.Steps.Skip(index + 1).Any(step => step.Kind == StepKind.Timetable))
    {
      known = false;
      return null;
    }
    return Current(locomotive);
  }

  private static int Start(TimetableTrain train) => Minutes(train, 0, TimetableTimeType.Departure);

  private static int End(TimetableTrain train)
  {
    var last = train.Entries.Count - 1;
    var arrival = Minutes(train, last, TimetableTimeType.Arrival);
    return arrival != int.MaxValue ? arrival : Minutes(train, last, TimetableTimeType.Departure);
  }

  private static int Minutes(TimetableTrain train, int index, TimetableTimeType type)
  {
    try
    {
      return train.TryGetAbsoluteTimeForEntry(index, type, out var minutes) ? minutes % MinutesPerDay : int.MaxValue;
    }
    catch (Exception)
    {
      return int.MaxValue;
    }
  }
}
