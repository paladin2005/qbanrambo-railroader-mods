using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.AI.Waypoints;

namespace QbanRambo.TrainOrders;

internal enum StepKind
{
  Move,
  Cut,
  WaitLoad,
  WaitUnload,
  WaitDay,
  WaitHours,
  Station,
  Refuel,
  Turntable,
  PickUp,
  CoupleBack,
  WaitRepair,
  WaitMinutes,
  RunAround,
  WaitTime,
  CoupleTo,
  SetSpeed,
  GoBack,
  Timetable
}

internal enum CutSide
{
  Auto,
  Front,
  Rear
}

internal sealed class OrderStep
{
  private const string KindKey = "kind";
  private const string WaypointKey = "wpt";
  private const string CountKey = "count";
  private const string CutCarKey = "cutCar";
  private const string SideKey = "side";
  private const string SmartKey = "smart";
  private const string CabKey = "cab";
  private const string UntilKey = "until";
  private const string StationKey = "station";
  private const string FuelKey = "fuel";
  private const string TurntableKey = "turntable";
  private const string ExitKey = "exit";
  private const string TurntableStateKey = "tt";
  private const string KeepKey = "keep";
  private const string TrackKey = "track";
  private const string FarKey = "far";
  private const string IdKey = "id";
  private const string FromKey = "from";
  private const string LeaveKey = "leave";
  private const string TimesKey = "times";
  private const string LapsKey = "laps";
  private const string SymbolKey = "symbol";
  internal const int DefaultBelow = 50;
  internal const int MorningHour = -1;
  private const int MinutesPerHour = 60;
  private const int MinutesPerDay = 24 * MinutesPerHour;

  private OrderStep(StepKind kind, OrderWaypoint? waypoint, int count, CutSide side = CutSide.Auto, string cutCarId = null, bool? smart = null, bool cabRelative = false, int? untilMinute = null, string stationId = null, string fuel = null, string turntableId = null, int? exit = null, string turntableState = null, bool keep = false, string track = null, string farCarId = null, string id = null, string from = null, int? leave = null, int? times = null, int? laps = null, string symbol = null)
  {
    Kind = kind;
    Waypoint = waypoint;
    Count = count;
    Side = side;
    CutCarId = cutCarId;
    Smart = smart;
    CabRelative = cabRelative;
    UntilMinute = untilMinute;
    StationId = stationId;
    Fuel = fuel;
    TurntableId = turntableId;
    Exit = exit;
    TurntableState = turntableState;
    Keep = keep;
    Track = track;
    FarCarId = farCarId;
    Id = id;
    From = from;
    Leave = leave;
    Times = times;
    Laps = laps;
    Symbol = symbol;
  }

  public StepKind Kind { get; }
  public OrderWaypoint? Waypoint { get; }
  public int Count { get; }
  public CutSide Side { get; }
  public string CutCarId { get; }
  public bool? Smart { get; }
  public bool CabRelative { get; }
  public int? UntilMinute { get; }
  public string StationId { get; }
  public string Fuel { get; }
  public string TurntableId { get; }
  public int? Exit { get; }
  public string TurntableState { get; }
  public bool Keep { get; }
  public string Track { get; }
  public string FarCarId { get; }
  public string Id { get; }
  public string From { get; }
  public int? Leave { get; }
  public int? Times { get; }
  public int? Laps { get; }
  public string Symbol { get; }

  public bool LeavesCars => Kind == StepKind.Cut || ((Kind == StepKind.PickUp || IsCouple) && Count != Maneuvers.AllCars) || (IsCouple && Leave.HasValue);

  public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

  public IReadOnlyList<string> FuelIds => string.IsNullOrEmpty(Fuel) ? new string[0] : Fuel.Split(',');

  public string Key => Kind switch
  {
    StepKind.Move => $"move|{Waypoint?.LocationString}|{Waypoint?.CoupleToCarId}|{Smart}",
    StepKind.Cut => $"cut|{Count}|{Side}|{Keep}",
    StepKind.WaitHours => $"hours|{Count}",
    StepKind.WaitMinutes => $"minutes|{Count}",
    StepKind.WaitDay => $"day|{Count}",
    StepKind.WaitTime => $"time|{Count}",
    StepKind.Station => $"station|{StationId}|{Track}",
    StepKind.Refuel => $"refuel|{Fuel}|{Count}",
    StepKind.Turntable => $"turntable|{TurntableId}|{Exit}|{TenderFirst}",
    StepKind.PickUp => $"pickup|{Count}|{Side}",
    StepKind.CoupleBack => $"coupleback|{Side}",
    StepKind.RunAround => $"runaround|{Side}",
    StepKind.CoupleTo => $"coupleto|{From}|{Side}",
    StepKind.SetSpeed => $"speed|{Count}",
    StepKind.GoBack => $"back|{From}|{Times}|{Count}",
    StepKind.Timetable => $"timetable|{Count}|{Symbol}",
    _ => Kind.ToString()
  };

  public static int Minute(GameDateTime time) => (int)(time.TotalSeconds / 60);

  public int UntilFrom(GameDateTime now)
  {
    var minute = Minute(now);
    switch (Kind)
    {
      case StepKind.WaitDay:
      case StepKind.WaitTime:
      case StepKind.GoBack:
        var target = now.Day * MinutesPerDay + (Kind == StepKind.WaitDay ? WaitHour * MinutesPerHour : Count);
        return target > minute ? target : target + MinutesPerDay;
      case StepKind.WaitMinutes:
        return minute + Count;
      default:
        return minute + Count * MinutesPerHour;
    }
  }

  public int WaitHour => Kind != StepKind.WaitDay ? 0 : Count == MorningHour ? MorningServeHour() : Count;

  private static int MorningServeHour()
  {
    var storage = StateManager.Shared?.Storage;
    return storage != null ? storage.InterchangeServeHour : 6;
  }

  public static OrderStep Move(Car locomotive, OrderWaypoint waypoint, bool? smart)
  {
    if (smart.HasValue) return new OrderStep(StepKind.Move, new OrderWaypoint(waypoint.LocationString, null), 0, smart: smart);
    var intent = new OrderWaypoint(waypoint.LocationString, waypoint.CoupleToCarId, CabEnd(locomotive, waypoint.ArrivingEnd), waypoint.BodyToward, null, 0f, waypoint.CoupleToCarEnd);
    return new OrderStep(StepKind.Move, intent, 0, cabRelative: true);
  }

  public OrderStep WithSmart(bool smart) => Kind == StepKind.Move && Waypoint.HasValue && Smart.HasValue ? new OrderStep(Kind, Waypoint, 0, smart: smart, id: Id) : this;

  public bool IsCouple => Kind == StepKind.Move && Waypoint is { WantsCouple: true };

  public OrderStep WithTake(int count) => IsCouple ? new OrderStep(Kind, Waypoint, count, Side, CutCarId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, Id, From) : this;

  public OrderStep WithLeave(int leave) => IsCouple ? new OrderStep(Kind, Waypoint, 0, Side, CutCarId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, Id, From, Math.Max(0, leave)) : this;

  public WaypointEnd ArrivingEndFor(Car locomotive) => CabRelative ? CabEnd(locomotive, Waypoint.Value.ArrivingEnd) : Waypoint.Value.ArrivingEnd;

  public WaypointEnd CabArrivingEnd(Car locomotive) => CabRelative ? Waypoint.Value.ArrivingEnd : CabEnd(locomotive, Waypoint.Value.ArrivingEnd);

  private static WaypointEnd CabEnd(Car locomotive, WaypointEnd end) => locomotive.FrontIsA ? end : PosedWaypoint.Flipped(end);

  public static OrderStep Cut(int count, CutSide side, bool keep = false) => new(StepKind.Cut, null, count, side, keep: keep);

  public static OrderStep CoupleBack(CutSide side = CutSide.Auto) => new(StepKind.CoupleBack, null, 0, side);

  public static OrderStep CoupleTo(string from, CutSide side = CutSide.Auto) => new(StepKind.CoupleTo, null, 0, side, from: from);

  public bool HasCoupleSide => Kind is StepKind.PickUp or StepKind.CoupleBack or StepKind.CoupleTo;

  public OrderStep WithSide(CutSide side) => HasCoupleSide ? new OrderStep(Kind, null, Count, side, id: Id, from: From) : this;

  public static OrderStep RunAround(CutSide side) => new(StepKind.RunAround, null, 0, side == CutSide.Rear ? CutSide.Rear : CutSide.Front);

  public OrderStep WithRunAround(string cutCarId, string farCarId, Car.End farEnd) => new(Kind, null, 0, Side, cutCarId, farCarId: $"{farCarId}|{farEnd}", id: Id);

  public string FarId => FarCarId?.Split('|')[0];

  public Car.End? FarEnd => FarCarId != null && FarCarId.Contains("|") ? FarCarId.EndsWith("|R") ? Car.End.R : Car.End.F : null;

  public static OrderStep PickUp(int count, CutSide side = CutSide.Auto) => new(StepKind.PickUp, null, count, side);

  public static OrderStep WaitLoad() => new(StepKind.WaitLoad, null, 0);

  public static OrderStep WaitUnload() => new(StepKind.WaitUnload, null, 0);

  public static OrderStep WaitDay() => new(StepKind.WaitDay, null, 0);

  public static OrderStep WaitHours(int hours) => new(StepKind.WaitHours, null, hours);

  public static OrderStep WaitMorning() => new(StepKind.WaitDay, null, MorningHour);

  public static OrderStep WaitMinutes(int minutes) => new(StepKind.WaitMinutes, null, minutes);

  public static OrderStep WaitTime(int minuteOfDay) => new(StepKind.WaitTime, null, minuteOfDay);

  public const int MaxSpeed = -1;

  public const int LeaveAll = 1000;

  public static OrderStep GoBackUntil(string targetId, int minuteOfDay) => new(StepKind.GoBack, null, minuteOfDay, from: targetId);

  public static OrderStep GoBackTimes(string targetId, int times) => new(StepKind.GoBack, null, 0, from: targetId, times: Math.Max(1, times));

  public OrderStep WithLaps(int laps) => new(Kind, Waypoint, Count, Side, CutCarId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, Id, From, Leave, Times, laps, Symbol);

  public OrderStep WithGoBackTime(int minuteOfDay) => Kind == StepKind.GoBack && !Times.HasValue ? GoBackUntil(From, minuteOfDay).WithId(Id) : this;

  public static OrderStep SetSpeed(int mph) => new(StepKind.SetSpeed, null, mph == 0 ? MaxSpeed : mph);

  public static OrderStep WaitRepair() => new(StepKind.WaitRepair, null, 0);

  public const int TimetableTrain = 0;

  public const int TimetableNone = 1;

  public const int TimetableNext = 2;

  public static OrderStep ForTimetable(int mode, string symbol) => new(StepKind.Timetable, null, mode is TimetableNone or TimetableNext ? mode : TimetableTrain, symbol: mode is TimetableNone or TimetableNext ? null : symbol);

  public OrderStep WithTimetable(int mode, string symbol) => Kind == StepKind.Timetable ? ForTimetable(mode, symbol).WithId(Id) : this;

  public static OrderStep ToStation(string stationId, string track = null) => new(StepKind.Station, null, 0, stationId: stationId, track: track);

  public OrderStep WithTrack(string track) => Kind == StepKind.Station ? ToStation(StationId, string.IsNullOrEmpty(track) ? null : track).WithId(Id) : this;

  public static OrderStep Refuel(IEnumerable<string> loaderIds, int below) => new(StepKind.Refuel, null, below, fuel: string.Join(",", loaderIds));

  public OrderStep WithBelow(int below) => Kind == StepKind.Refuel ? new OrderStep(Kind, null, below, fuel: Fuel, id: Id) : this;

  public static OrderStep Turntable(string turntableId, int? exit, bool tenderFirst = false) => new(StepKind.Turntable, null, 0, exit.HasValue && tenderFirst ? CutSide.Rear : CutSide.Auto, turntableId: turntableId, exit: exit);

  public bool TenderFirst => Kind == StepKind.Turntable && Exit.HasValue && Side == CutSide.Rear;

  public OrderStep WithExit(int? exit) => Kind == StepKind.Turntable ? Turntable(TurntableId, exit, TenderFirst).WithId(Id) : this;

  public OrderStep WithTenderFirst(bool tenderFirst) => Kind == StepKind.Turntable ? Turntable(TurntableId, Exit, tenderFirst).WithId(Id) : this;

  public OrderStep WithTurntableState(string state) => new(Kind, Waypoint, Count, Side, CutCarId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, state, Keep, Track, FarCarId, Id, From, Leave, Times, Laps, Symbol);

  public OrderStep WithCutCar(string carId) => new(Kind, Waypoint, Count, Side, carId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, Id, From, Leave, Times, Laps, Symbol);

  public OrderStep WithUntil(int minute) => new(Kind, Waypoint, Count, Side, CutCarId, Smart, CabRelative, minute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, Id, From, Leave, Times, Laps, Symbol);

  public OrderStep WithId(string id) => new(Kind, Waypoint, Count, Side, CutCarId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, id, From, Leave, Times, Laps, Symbol);

  public OrderStep WithFrom(string from) => new(Kind, Waypoint, Count, Side, CutCarId, Smart, CabRelative, UntilMinute, StationId, Fuel, TurntableId, Exit, TurntableState, Keep, Track, FarCarId, Id, from, Leave, Times, Laps, Symbol);

  public OrderStep WithTime(int minuteOfDay) => new(StepKind.WaitTime, null, minuteOfDay, id: Id);

  public OrderStep Fresh() => new(Kind, Waypoint, Count, Side, null, Smart, CabRelative, null, StationId, Fuel, TurntableId, Exit, keep: Keep, track: Track, id: Id, from: From, leave: Leave, times: Times, symbol: Symbol);

  public static List<OrderStep> Renumbered(IEnumerable<OrderStep> steps)
  {
    var list = steps.ToList();
    var ids = list.Where(step => step.Id != null).Select(step => step.Id).Distinct().ToDictionary(id => id, _ => NewId());
    return list.Select(step =>
    {
      var copy = step.Id != null ? step.WithId(ids[step.Id]) : step;
      return copy.From != null && ids.TryGetValue(copy.From, out var from) ? copy.WithFrom(from) : copy;
    }).ToList();
  }

  public Value ToValue()
  {
    var values = new Dictionary<string, Value> { { KindKey, (int)Kind } };
    if (Waypoint.HasValue) values[WaypointKey] = Waypoint.Value.PropertyValue;
    if (Kind is StepKind.Cut or StepKind.WaitHours or StepKind.WaitMinutes or StepKind.WaitTime or StepKind.Refuel or StepKind.PickUp or StepKind.SetSpeed or StepKind.GoBack || ((IsCouple || Kind is StepKind.WaitDay or StepKind.Timetable) && Count != 0)) values[CountKey] = Count;
    if (Side != CutSide.Auto) values[SideKey] = (int)Side;
    if (!string.IsNullOrEmpty(CutCarId)) values[CutCarKey] = CutCarId;
    if (Smart.HasValue) values[SmartKey] = Smart.Value;
    if (CabRelative) values[CabKey] = true;
    if (UntilMinute.HasValue) values[UntilKey] = UntilMinute.Value;
    if (!string.IsNullOrEmpty(StationId)) values[StationKey] = StationId;
    if (!string.IsNullOrEmpty(Fuel)) values[FuelKey] = Fuel;
    if (!string.IsNullOrEmpty(TurntableId)) values[TurntableKey] = TurntableId;
    if (Exit.HasValue) values[ExitKey] = Exit.Value;
    if (Keep) values[KeepKey] = true;
    if (!string.IsNullOrEmpty(TurntableState)) values[TurntableStateKey] = TurntableState;
    if (!string.IsNullOrEmpty(Track)) values[TrackKey] = Track;
    if (!string.IsNullOrEmpty(FarCarId)) values[FarKey] = FarCarId;
    if (!string.IsNullOrEmpty(Id)) values[IdKey] = Id;
    if (!string.IsNullOrEmpty(From)) values[FromKey] = From;
    if (Leave.HasValue) values[LeaveKey] = Leave.Value;
    if (Times.HasValue) values[TimesKey] = Times.Value;
    if (Laps.HasValue) values[LapsKey] = Laps.Value;
    if (!string.IsNullOrEmpty(Symbol)) values[SymbolKey] = Symbol;
    return Value.Dictionary(values);
  }

  public static OrderStep FromValue(Value value)
  {
    if (value.IsNull) return null;
    var kind = (StepKind)value[KindKey].IntValueOrDefault(0);
    var step = kind switch
    {
      StepKind.Move => OrderWaypoint.FromPropertyValue(value[WaypointKey]) is { } waypoint ? new OrderStep(kind, waypoint, waypoint.WantsCouple ? value[CountKey].IntValueOrDefault(0) : 0, smart: value[SmartKey].IsNull ? null : value[SmartKey].BoolValueOrDefault(false), cabRelative: value[CabKey].BoolValueOrDefault(false), id: IdOf(value), leave: waypoint.WantsCouple && !value[LeaveKey].IsNull ? value[LeaveKey].IntValueOrDefault(0) : null) : null,
      StepKind.Cut => new OrderStep(kind, null, value[CountKey].IntValueOrDefault(0), (CutSide)value[SideKey].IntValueOrDefault(0), NullIfEmpty(value[CutCarKey].StringValueOrDefault(null)), keep: value[KeepKey].BoolValueOrDefault(false), id: IdOf(value)),
      StepKind.CoupleBack => CoupleBack(SideOf(value)),
      StepKind.SetSpeed => SetSpeed(value[CountKey].IntValueOrDefault(MaxSpeed)),
      StepKind.GoBack => NullIfEmpty(value[FromKey].StringValueOrDefault(null)) is { } target ? new OrderStep(kind, null, value[CountKey].IntValueOrDefault(0), untilMinute: value[UntilKey].IsNull ? null : value[UntilKey].IntValueOrDefault(0), id: IdOf(value), from: target, times: value[TimesKey].IsNull ? null : value[TimesKey].IntValueOrDefault(1), laps: value[LapsKey].IsNull ? null : value[LapsKey].IntValueOrDefault(0)) : null,
      StepKind.CoupleTo => NullIfEmpty(value[FromKey].StringValueOrDefault(null)) is { } from ? CoupleTo(from, SideOf(value)) : null,
      StepKind.RunAround => new OrderStep(kind, null, 0, (CutSide)value[SideKey].IntValueOrDefault((int)CutSide.Front), NullIfEmpty(value[CutCarKey].StringValueOrDefault(null)), farCarId: NullIfEmpty(value[FarKey].StringValueOrDefault(null))),
      StepKind.PickUp => new OrderStep(kind, null, value[CountKey].IntValueOrDefault(0), SideOf(value), cutCarId: NullIfEmpty(value[CutCarKey].StringValueOrDefault(null)), id: IdOf(value)),
      StepKind.WaitLoad => WaitLoad(),
      StepKind.WaitUnload => WaitUnload(),
      StepKind.WaitRepair => WaitRepair(),
      StepKind.WaitDay or StepKind.WaitHours or StepKind.WaitMinutes or StepKind.WaitTime => new OrderStep(kind, null, value[CountKey].IntValueOrDefault(0), untilMinute: value[UntilKey].IsNull ? null : value[UntilKey].IntValueOrDefault(0)),
      StepKind.Station => NullIfEmpty(value[StationKey].StringValueOrDefault(null)) is { } stationId ? ToStation(stationId, NullIfEmpty(value[TrackKey].StringValueOrDefault(null))) : null,
      StepKind.Refuel => NullIfEmpty(value[FuelKey].StringValueOrDefault(null)) is { } fuel ? new OrderStep(kind, null, value[CountKey].IntValueOrDefault(DefaultBelow), fuel: fuel) : null,
      StepKind.Timetable => ForTimetable(value[CountKey].IntValueOrDefault(TimetableTrain), NullIfEmpty(value[SymbolKey].StringValueOrDefault(null))),
      StepKind.Turntable => NullIfEmpty(value[TurntableKey].StringValueOrDefault(null)) is { } turntable ? new OrderStep(kind, null, 0, SideOf(value) == CutSide.Rear && !value[ExitKey].IsNull ? CutSide.Rear : CutSide.Auto, turntableId: turntable, exit: value[ExitKey].IsNull ? null : value[ExitKey].IntValueOrDefault(0), turntableState: NullIfEmpty(value[TurntableStateKey].StringValueOrDefault(null))) : null,
      _ => null
    };
    return step != null && step.Id == null && IdOf(value) is { } id ? step.WithId(id) : step;
  }

  private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

  private static CutSide SideOf(Value value) => value[SideKey].IntValueOrDefault(0) switch
  {
    (int)CutSide.Front => CutSide.Front,
    (int)CutSide.Rear => CutSide.Rear,
    _ => CutSide.Auto
  };

  private static string IdOf(Value value) => NullIfEmpty(value[IdKey].StringValueOrDefault(null));
}

internal sealed class OrderList
{
  internal const string Key = "qbanrambo.train-orders";
  internal const string DoneStatus = "All orders done";

  private const string RunningKey = "run";
  private const string StatusKey = "status";
  private const string RepeatKey = "repeat";
  private const string PassSignalsKey = "pass";
  private const string LeftKey = "left";
  private const string CurrentKey = "at";
  private const string StepsKey = "steps";
  private const string CutsKey = "cuts";
  private const string SpeedKey = "mph";

  public bool Running { get; set; }
  public string Status { get; set; }
  public bool Repeat { get; set; }
  public bool PassSignals { get; set; }
  public string LeftCarId { get; set; }
  public int Speed { get; set; }
  public int Current { get; private set; }
  public List<OrderStep> Steps { get; } = new();
  public Dictionary<string, string> Cuts { get; } = new();

  public void NoteLeft(OrderStep step, string carId)
  {
    LeftCarId = carId;
    if (step?.Id != null) Cuts[step.Id] = carId;
  }

  public int IndexOfId(string id) => id == null ? -1 : Steps.FindIndex(step => step.Id == id);

  public List<string> Stations => StationsIn(Steps);

  public List<string> StationsAhead => StationsIn(Repeat ? Steps : Steps.Skip(Current));

  private static List<string> StationsIn(IEnumerable<OrderStep> steps) => steps.Where(step => step.Kind == StepKind.Station).Select(step => step.StationId).Distinct().ToList();

  public void Add(OrderStep step)
  {
    Steps.Add(step);
    if (!Running && Status == DoneStatus) Status = null;
  }

  public bool Finished => Current >= Steps.Count;

  public void JumpTo(int index)
  {
    if (index >= 0 && index < Steps.Count) Current = index;
  }

  public bool Advance()
  {
    if (Finished) return true;
    Steps[Current] = Steps[Current].Fresh();
    Current++;
    if (!Finished) return false;
    if (!Repeat) return true;
    Current = 0;
    return false;
  }

  public void Start()
  {
    if (Finished) Current = 0;
    Running = true;
    Status = null;
  }

  public void Finish()
  {
    Running = false;
    Status = DoneStatus;
  }

  public void RemoveAt(int index)
  {
    if (index < 0 || index >= Steps.Count) return;
    Steps.RemoveAt(index);
    if (index < Current) Current--;
    if (Repeat && Finished) Current = 0;
  }

  public void Move(int index, int offset)
  {
    var target = index + offset;
    if (index < 0 || index >= Steps.Count || target < 0 || target >= Steps.Count) return;
    (Steps[index], Steps[target]) = (Steps[target], Steps[index]);
    if (Current == index) Current = target;
    else if (Current == target) Current = index;
  }

  public void Clear()
  {
    Steps.Clear();
    Cuts.Clear();
    Speed = 0;
    Current = 0;
  }

  public static OrderList Read(Car locomotive)
  {
    var list = new OrderList();
    var value = locomotive.KeyValueObject[Key];
    if (value.IsNull) return list;
    list.Running = value[RunningKey].BoolValueOrDefault(false);
    var status = value[StatusKey].StringValueOrDefault(null);
    list.Status = string.IsNullOrEmpty(status) ? null : status;
    list.Repeat = value[RepeatKey].BoolValueOrDefault(false);
    list.PassSignals = value[PassSignalsKey].BoolValueOrDefault(false);
    var left = value[LeftKey].StringValueOrDefault(null);
    list.LeftCarId = string.IsNullOrEmpty(left) ? null : left;
    list.Speed = value[SpeedKey].IntValueOrDefault(0);
    var steps = value[StepsKey];
    if (!steps.IsNull) list.Steps.AddRange(steps.ArrayValue.Select(OrderStep.FromValue).Where(step => step != null));
    var cuts = value[CutsKey];
    if (!cuts.IsNull)
    {
      foreach (var pair in cuts.DictionaryValue)
      {
        var carId = pair.Value.StringValueOrDefault(null);
        if (!string.IsNullOrEmpty(carId)) list.Cuts[pair.Key] = carId;
      }
    }
    var current = value[CurrentKey].IntValueOrDefault(0);
    list.Current = current >= 0 && current <= list.Steps.Count ? current : 0;
    return list;
  }

  public void Write(Car locomotive)
  {
    if (Steps.Count == 0 && !Running && !Repeat && !PassSignals && string.IsNullOrEmpty(Status))
    {
      locomotive.KeyValueObject[Key] = Value.Null();
      return;
    }
    var values = new Dictionary<string, Value>
    {
      { RunningKey, Running },
      { StatusKey, Status ?? "" },
      { RepeatKey, Repeat },
      { PassSignalsKey, PassSignals },
      { CurrentKey, Current },
      { StepsKey, Value.Array(Steps.Select(step => step.ToValue()).ToList()) }
    };
    if (!string.IsNullOrEmpty(LeftCarId)) values[LeftKey] = LeftCarId;
    if (Speed != 0) values[SpeedKey] = Speed;
    var cuts = Cuts.Where(pair => IndexOfId(pair.Key) >= 0).ToDictionary(pair => pair.Key, pair => (Value)pair.Value);
    if (cuts.Count > 0) values[CutsKey] = Value.Dictionary(cuts);
    locomotive.KeyValueObject[Key] = Value.Dictionary(values);
  }
}
