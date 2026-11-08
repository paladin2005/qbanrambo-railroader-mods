using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using Model.AI;
using UI;
using UI.Builder;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace QbanRambo.TrainOrders;

internal sealed class OrdersWindow : BuilderWindowBase
{
  private const int Width = 480;
  private const int DefaultHeight = 560;
  private const int MinHeight = 340;
  private const int MaxHeight = 1600;
  private const float BelowWidth = 100f;
  private const float ThenWidth = 124f;
  private const float ExitWidth = 200f;
  private const float TimeWidth = 70f;
  private const float RowButtonSpacing = 10f;
  private const float IconButtonSpacing = 4f;
  private const float StripButtonSpacing = 12f;
  private const float RowSpacing = 10f;
  private const float StepLineSpacing = -12f;
  private const float RefuelLineSpacing = 0f;
  private const float SmartIndent = 16f;
  private const float BlockSpacing = 8f;
  private const string MenuOwner = "Train Orders";
  private const string GotoMenuKey = "qbanrambo.goto.menu";
  private const float CompositionCheckSeconds = 0.5f;
  private const string PlainStyle = "{0}";
  private const string DoneStyle = "<alpha=#33>{0}";
  private const string CurrentStyle = "<color=#FFC060>{0}</color>";

  private const string BaseIdentifier = "qbanrambo.train-orders";
  private const float CascadeOffset = 32f;
  private const float SamePositionDistance = 4f;
  private const string OrdersTab = "orders";
  private const string JobsTab = "jobs";

  private static readonly List<OrdersWindow> Windows = new();
  private static int _pendingSlot = 1;
  private static PopupMenu _menu;
  private static OrdersWindow _follower;
  private static BaseLocomotive _seenSelection;

  private int _slot = 1;
  private BaseLocomotive _locomotive;
  private (int Front, int Rear) _composition;
  private float _nextCompositionCheck;
  private RectTransform _list;
  private ForecastRun _forecast;
  private float _listPosition = 1f;
  private bool _restorePending;
  private readonly UIState<string> _tab = new(OrdersTab);
  private string _jobName;

  public override string Identifier => _slot == 1 ? BaseIdentifier : $"{BaseIdentifier}.{_slot}";

  public override WindowConfig Config => WindowConfig.Resizable(new Vector2Int(Width, DefaultHeight), new Vector2Int(Width, MinHeight), new Vector2Int(Width, MaxHeight), Window.Position.CenterRight);

  private bool IsShown => Window != null && Window.IsShown;

  internal bool IsOpen => IsShown && _locomotive != null;

  internal static bool IsOpenFor(Car car)
  {
    return car != null && (WebOrders.Capturing == car || Windows.Any(window => window._locomotive == car && window.IsShown));
  }

  internal static void Present(BaseLocomotive locomotive)
  {
    var manager = WindowManager.Shared;
    if (manager == null || locomotive == null) return;
    var window = Windows.FirstOrDefault(open => open._locomotive == locomotive && open.IsShown);
    if (window != null)
    {
      window.Window.OrderFront();
      Follow(window);
      return;
    }
    if (TrainOrdersSettings.OneWindow && Follower() is { } follower)
    {
      follower.SwitchTo(locomotive);
      follower.Window.OrderFront();
      Follow(follower);
      return;
    }
    window = Windows.FirstOrDefault(closed => closed._locomotive == locomotive && !closed.IsShown) ?? Windows.FirstOrDefault(closed => !closed.IsShown);
    if (window == null)
    {
      _pendingSlot = Enumerable.Range(1, Windows.Count + 1).First(slot => Windows.All(existing => existing._slot != slot));
      window = manager.CreateWindowWithComponent<OrdersWindow>();
    }
    window._locomotive = locomotive;
    window.Title = $"Train Orders: {locomotive.DisplayName}";
    window.Show();
    window.Window.OrderFront();
    window.Cascade();
    Follow(window);
  }

  private static OrdersWindow Follower() => _follower != null && _follower.IsShown ? _follower : Windows.FirstOrDefault(window => window.IsShown);

  private static void Follow(OrdersWindow window)
  {
    _follower = window;
    _seenSelection = TrainController.Shared?.SelectedLocomotive;
  }

  private void SwitchTo(BaseLocomotive locomotive)
  {
    if (locomotive == null || locomotive == _locomotive) return;
    Ghosts.Clear(this);
    _locomotive = locomotive;
    _listPosition = 1f;
    _jobName = null;
    Title = $"Train Orders: {locomotive.DisplayName}";
    RebuildPanel();
  }

  private void FollowSelection()
  {
    var selected = TrainController.Shared?.SelectedLocomotive;
    if (selected == _seenSelection) return;
    _seenSelection = selected;
    if (selected == null || selected.IsInBardo || Windows.Any(other => other != this && other.IsShown && other._locomotive == selected)) return;
    SwitchTo(selected);
  }

  internal static void RebuildAll()
  {
    foreach (var window in Windows.Where(window => window.IsShown).ToList()) window.RebuildPanel();
  }

  internal static void CloseAll()
  {
    _follower = null;
    _seenSelection = null;
    foreach (var window in Windows.ToList())
    {
      window._locomotive = null;
      if (window.IsShown) window.Window.CloseWindow();
    }
  }

  protected override void OnWindowAwake()
  {
    _slot = _pendingSlot;
    Windows.Add(this);
  }

  private void OnDestroy()
  {
    if (_follower == this) _follower = null;
    Windows.Remove(this);
    Ghosts.Clear(this);
  }

  private void Cascade()
  {
    var start = Window.GetPosition();
    var position = start;
    for (var step = 0; step < Windows.Count && Windows.Any(other => other != this && other.IsShown && Vector2.Distance(other.Window.GetPosition(), position) < SamePositionDistance); step++)
    {
      position += new Vector2(CascadeOffset, -CascadeOffset);
    }
    if (position != start) Window.SetPositionRestoring(position);
  }

  private void Update()
  {
    if (_list != null && _list.TryGetComponent<ScrollRect>(out var scroll))
    {
      if (_restorePending)
      {
        _restorePending = false;
        scroll.verticalNormalizedPosition = _listPosition;
      }
      else
      {
        _listPosition = scroll.verticalNormalizedPosition;
      }
    }
    if (_locomotive != null && Window != null && Window.IsShown && this == Follower() && TrainOrdersSettings.OneWindow)
    {
      try
      {
        FollowSelection();
      }
      catch (Exception exception)
      {
        TrainOrdersMod.Log?.Error(exception, "Couldn't switch the Train Orders window to the selected train");
        _seenSelection = TrainController.Shared?.SelectedLocomotive;
      }
    }
    if (_locomotive == null || Window == null || !Window.IsShown || Time.unscaledTime < _nextCompositionCheck) return;
    _nextCompositionCheck = Time.unscaledTime + CompositionCheckSeconds;
    try
    {
      if (Maneuvers.CarsAround(_locomotive) != _composition) RebuildPanel();
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't refresh the Train Orders window");
      _nextCompositionCheck = float.MaxValue;
    }
  }

  protected override void BuildContent(UIPanelBuilder builder)
  {
    var locomotive = _locomotive;
    _list = null;
    if (locomotive == null)
    {
      Ghosts.Clear(this);
      builder.AddLabelEmptyState("The locomotive is gone.");
      return;
    }
    builder.AddTabbedPanels(_tab, tabs => tabs
      .AddTab("Orders", OrdersTab, tab => BuildOrders(tab, locomotive))
      .AddTab("Saved jobs", JobsTab, tab => BuildJobs(tab, locomotive)));
  }

  private void BuildOrders(UIPanelBuilder builder, BaseLocomotive locomotive)
  {
    builder.ConfigureLayout(BlockSpacing);
    builder.RebuildOnPropertyChange(locomotive.KeyValueObject, OrderList.Key);
    var orders = OrderList.Read(locomotive);
    var forecast = SafeForecast(locomotive, orders);
    _forecast = forecast;
    Ghosts.Clear(this);
    _composition = Maneuvers.CarsAround(locomotive);
    builder.AddField("Status", () => OrderActions.Status(OrderList.Read(locomotive)), UIPanelBuilder.Frequency.Periodic);
    builder.AddField("Engineer", () => EngineerStatus(locomotive), UIPanelBuilder.Frequency.Periodic);
    if (_composition.Front > 0 && _composition.Rear > 0) builder.AddField("Train", $"{Cars(_composition.Front)} in front, {Cars(_composition.Rear)} behind");
    builder.HStack(row =>
    {
      row.AddField("Repeat", row.AddToggle(() => orders.Repeat, repeat => OrderActions.Run(() => OrderActions.SetRepeat(locomotive, repeat))))
        .Tooltip("Repeat", "Each order goes back to the end of the list when it's done, so the orders run over and over.");
      row.AddField("Smart signals", row.AddToggle(() => orders.PassSignals, pass => OrderActions.Run(() => OrderActions.SetPassSignals(locomotive, pass))))
        .Tooltip("Smart signals", "In ABS mode, when the train has waited 10 seconds at a red signal at a switch, the engineer passes it at restricted speed, but only when no other train is in the blocks ahead. With another train there, it keeps waiting.");
    });
    builder.AddLabel($"While this window is open, waypoints you set for {locomotive.DisplayName} are added to its orders.");
    builder.AddSection("Orders", section =>
    {
      section.ConfigureLayout(RowSpacing);
      if (orders.Steps.Count == 0)
      {
        section.AddLabelEmptyState("No orders yet");
        return;
      }
      var icons = Icons.EnsureButtons();
      var started = orders.Running || orders.Status != null;
      _restorePending = true;
      _list = section.VScrollView(list =>
      {
        list.ConfigureLayout(RowSpacing);
        for (var i = 0; i < orders.Steps.Count; i++)
        {
          var index = i;
          var step = orders.Steps[index];
          var text = $"{index + 1}. {OrderActions.RowText(step, orders)}";
          var style = index < orders.Current ? DoneStyle : index == orders.Current && started ? CurrentStyle : PlainStyle;
          Func<string> progress = step.Kind switch
          {
            StepKind.WaitLoad => () => OrderActions.LoadProgress(locomotive, false),
            StepKind.WaitUnload => () => OrderActions.LoadProgress(locomotive, true),
            StepKind.WaitDay or StepKind.WaitHours or StepKind.WaitMinutes or StepKind.WaitTime => () => OrderActions.TimeLeft(step),
            StepKind.WaitRepair => () => Repairs.Progress(locomotive),
            StepKind.Move or StepKind.Station => () => Dispatcher.IsBoarding(locomotive) ? "boarding" : null,
            StepKind.Refuel => () => Dispatcher.RefuelDetail(locomotive),
            StepKind.Turntable => () => Dispatcher.TurntableDetail(locomotive),
            _ => null
          };
          if (index != orders.Current) progress = null;
          var hasForecast = forecast.Results.TryGetValue(index, out var result);
          var row = list.VStack(entry =>
          {
            var tracks = step.Kind == StepKind.Station ? OrderActions.TrackOptions(step.StationId) : null;
            var hasTracks = tracks != null && tracks.Count > 2;
            entry.ConfigureLayout(step.Kind is StepKind.Refuel or StepKind.Turntable || OrderActions.HasTime(step) || step.IsCouple || hasForecast || hasTracks ? RefuelLineSpacing : StepLineSpacing);
            KeepHeights(entry.HStack(row =>
            {
              row.ConfigureLayout(RowButtonSpacing);
              var label = progress != null
                ? row.AddLabel(() => string.Format(style, progress() is { } detail ? $"{text} ({detail})" : text), UIPanelBuilder.Frequency.Periodic)
                : row.AddLabel(string.Format(style, text));
              label.FlexibleWidth();
              KeepHeights(row.HStack(buttons =>
              {
                buttons.ConfigureLayout(IconButtonSpacing);
                IconButton(buttons, icons, Icons.UpName, "Up", "Move up", "Run this order earlier", () => OrderActions.Move(locomotive, index, -1)).Disable(index == 0);
                IconButton(buttons, icons, Icons.DownName, "Down", "Move down", "Run this order later", () => OrderActions.Move(locomotive, index, 1)).Disable(index == orders.Steps.Count - 1);
                IconButton(buttons, icons, Icons.TrashName, "Remove", "Remove", "Remove this order", () => OrderActions.Remove(locomotive, index));
              }));
            }));
            if (step.Kind == StepKind.Refuel)
            {
              entry.HStack(line =>
              {
                line.ConfigureLayout(RowButtonSpacing);
                line.Spacer(SmartIndent);
                line.AddLabel(string.Format(style == CurrentStyle ? PlainStyle : style, "Only below"));
                var choice = Array.IndexOf(OrderActions.BelowOptions, step.Count);
                line.AddDropdown(OrderActions.BelowOptions.Select(OrderActions.BelowLabel).ToList(), choice < 0 ? 1 : choice, picked =>
                {
                  if (picked >= 0 && picked < OrderActions.BelowOptions.Length && OrderActions.BelowOptions[picked] != step.Count) OrderActions.Run(() => OrderActions.SetBelow(locomotive, index, OrderActions.BelowOptions[picked]));
                })
                  .Width(BelowWidth);
                line.Spacer().FlexibleWidth();
              });
              return;
            }
            if (OrderActions.HasTime(step))
            {
              entry.HStack(line =>
              {
                line.ConfigureLayout(RowButtonSpacing);
                line.Spacer(SmartIndent);
                line.AddLabel(string.Format(style == CurrentStyle ? PlainStyle : style, "Time"));
                var hour = step.Count / 60;
                var minute = step.Count % 60;
                line.AddDropdown(OrderActions.HourLabels, hour, picked =>
                {
                  if (picked >= 0 && picked < OrderActions.HourLabels.Count && picked != hour) OrderActions.Run(() => OrderActions.SetTime(locomotive, index, picked * 60 + minute));
                })
                  .Width(TimeWidth);
                var minuteChoice = Array.IndexOf(OrderActions.MinuteOptions, minute);
                line.AddDropdown(OrderActions.MinuteOptions.Select(option => $":{option:D2}").ToList(), minuteChoice < 0 ? 0 : minuteChoice, picked =>
                {
                  if (picked >= 0 && picked < OrderActions.MinuteOptions.Length && OrderActions.MinuteOptions[picked] != minute) OrderActions.Run(() => OrderActions.SetTime(locomotive, index, hour * 60 + OrderActions.MinuteOptions[picked]));
                })
                  .Width(TimeWidth)
                  .Tooltip("Time", step.Kind == StepKind.GoBack ? "The game time until which this order goes back: it goes back while it's earlier than this, then goes on." : "The game time this order waits for: today if it hasn't come yet, else tomorrow.");
                line.Spacer().FlexibleWidth();
              });
              return;
            }
            if (step.IsCouple)
            {
              entry.HStack(line =>
              {
                line.ConfigureLayout(RowButtonSpacing);
                line.Spacer(SmartIndent);
                line.AddLabel(string.Format(style == CurrentStyle ? PlainStyle : style, "Then"));
                var options = OrderActions.TakeOptions();
                var current = OrderActions.TakeValue(step);
                var choice = options.FindIndex(option => option.Count == current);
                line.AddDropdown(options.Select(option => option.Label).ToList(), choice < 0 ? 0 : choice, picked =>
                {
                  if (picked >= 0 && picked < options.Count && options[picked].Count != current) OrderActions.Run(() => OrderActions.SetTake(locomotive, index, options[picked].Count));
                })
                  .Width(ThenWidth)
                  .Tooltip("After coupling", "Take all, or take that many of the cars it couples to, counted from the coupling; the rest stay with a handbrake set. Take none: uncouple right away, the cars stay. Leave N: leave N of the train's own cars, the ones next to the coupling, with the cars that were there; Leave all leaves every car on that side, up to the engine.");
                line.Spacer().FlexibleWidth();
              });
              if (hasForecast) AddForecast(entry, result);
              return;
            }
            if (hasForecast)
            {
              AddForecast(entry, result);
              return;
            }
            if (hasTracks)
            {
              entry.HStack(line =>
              {
                line.ConfigureLayout(RowButtonSpacing);
                line.Spacer(SmartIndent);
                var choice = tracks.FindIndex(option => option.Track == step.Track);
                line.AddDropdown(tracks.Select(option => option.Label).ToList(), choice < 0 ? 0 : choice, picked =>
                {
                  if (picked >= 0 && picked < tracks.Count && tracks[picked].Track != step.Track) OrderActions.Run(() => OrderActions.SetTrack(locomotive, index, tracks[picked].Track));
                })
                  .Width(ExitWidth)
                  .Tooltip("Track", "Which station track the train stops on. Shortest route takes whichever track the engineer reaches first, which may not be the one by the station building: pick that one by its side to be sure.");
                line.Spacer().FlexibleWidth();
              });
              return;
            }
            if (step.Kind == StepKind.Turntable)
            {
              entry.HStack(line =>
              {
                line.ConfigureLayout(RowButtonSpacing);
                line.Spacer(SmartIndent);
                var options = OrderActions.ExitOptions(Turntables.Find(step.TurntableId));
                var choice = options.FindIndex(option => option.Exit == step.Exit);
                line.AddDropdown(options.Select(option => option.Label).ToList(), choice < 0 ? 0 : choice, picked =>
                {
                  if (picked >= 0 && picked < options.Count && options[picked].Exit != step.Exit) OrderActions.Run(() => OrderActions.SetExit(locomotive, index, options[picked].Exit));
                })
                  .Width(ExitWidth);
                line.Spacer().FlexibleWidth();
              });
              return;
            }
            if (step.Kind != StepKind.Move || !step.Smart.HasValue) return;
            entry.HStack(line =>
            {
              line.ConfigureLayout(RowButtonSpacing);
              line.Spacer(SmartIndent);
              line.AddToggle(() => step.Smart == true, smart => OrderActions.Run(() => OrderActions.SetSmart(locomotive, index, smart)))
                .Tooltip("Smart entrance", "Push the cars into this dead end so the engine stays at the open end.");
              line.AddLabel(string.Format(style == CurrentStyle ? PlainStyle : style, "Smart entrance"));
              line.Spacer().FlexibleWidth();
            });
          });
          AddHover(row, index);
        }
      });
    });
    builder.AddSection("Add", section =>
    {
      section.HStack(row =>
      {
        var icons = Icons.EnsureButtons();
        if (AppDomain.CurrentDomain.GetData(GotoMenuKey) is Func<object, IReadOnlyList<MenuItem>> gotoMenu)
        {
          MenuButton(row, icons, Icons.GotoName, "Go to", "Go to", "Add a trip to a town location. Passenger stations become station orders, like the Stations button.", () => gotoMenu(locomotive));
        }
        MenuButton(row, icons, Icons.PickUpName, "Pick up", "Pick up cars", "Add an order to pick up cars: the train couples to the nearest standing cars and takes that many, leaving the rest.", () => OrderActions.PickUpItems(locomotive));
        MenuButton(row, icons, Icons.CutName, "Leave cars", "Leave cars", "Add an order to leave cars behind: the front or rear ones, a few or all of them.", () => OrderActions.CutItems(locomotive));
        MenuButton(row, icons, Icons.WaitName, "Wait", "Wait", "Add a wait: until the cars are loaded or unloaded, until tomorrow, or for some hours.", () => OrderActions.WaitItems(locomotive));
        MenuButton(row, icons, Icons.GoBackName, "Go back", "Go back", "Add a loop: go back to an earlier order until a time or a number of times, then go on.", () => OrderActions.GoBackItems(locomotive));
        MenuButton(row, icons, Icons.SpeedName, "Speed", "Set speed", "Add a speed: the trips after it run at that speed until another Set speed order.", () => OrderActions.SpeedItems(locomotive));
        MenuButton(row, icons, Icons.StationName, "Stations", "Stations", "Add a stop at a passenger station.", () => OrderActions.StationItems(locomotive));
        MenuButton(row, icons, Icons.RefuelName, "Refuel", "Refuel", "Add a refuel at a water, coal or diesel loader.", () => OrderActions.RefuelItems(locomotive));
        MenuButton(row, icons, Icons.TurntableName, "Turntable", "Turntable", "Add a turntable move: turn the engine around, park it on a stall or leave on another track. Engines only, no cars.", () => OrderActions.TurntableItems(locomotive));
        row.Spacer().FlexibleWidth();
      });
    });
    builder.ButtonStrip(strip =>
    {
      strip.ConfigureLayout(StripButtonSpacing);
      strip.AddButton(orders.Running ? "Pause" : "Start", () => OrderActions.Run(() => OrderActions.SetRunning(locomotive, !orders.Running))).Disable(orders.Steps.Count == 0);
      strip.AddButton("Skip", () => OrderActions.Run(() => OrderActions.Skip(locomotive))).Disable(orders.Finished);
      strip.AddButton("Clear", () => OrderActions.Run(() => OrderActions.Clear(locomotive))).Disable(orders.Steps.Count == 0);
      strip.Spacer().FlexibleWidth();
      var icons = Icons.EnsureButtons();
      IconButton(strip, icons, Icons.CopyName, "Copy", "Copy orders", "Copy these orders to paste them into another locomotive's orders", () => OrderActions.Copy(locomotive))
        .Disable(orders.Steps.Count == 0);
      IconButton(strip, icons, Icons.PasteName, "Paste", "Paste orders", OrderActions.HasCopy ? $"Add the {OrderActions.CopyDescription} to the end of these orders" : "Copy another locomotive's orders first", () => OrderActions.Paste(locomotive))
        .Disable(!OrderActions.HasCopy);
    });
  }

  private void BuildJobs(UIPanelBuilder builder, BaseLocomotive locomotive)
  {
    _list = null;
    Ghosts.Clear(this);
    try
    {
      builder.ConfigureLayout(BlockSpacing);
      builder.RebuildOnPropertyChange(locomotive.KeyValueObject, OrderList.Key);
      var orders = OrderList.Read(locomotive);
      var name = _jobName ?? OrderActions.DefaultJobName(orders);
      var icons = Icons.EnsureButtons();
      builder.AddSection("Save these orders", section =>
      {
        section.ConfigureLayout(RowSpacing);
        section.HStack(row =>
        {
          row.ConfigureLayout(RowButtonSpacing);
          row.AddInputField(name, value => _jobName = value, "Job name", SavedJobs.NameLimit).FlexibleWidth();
          row.AddButtonCompact("Save", () => OrderActions.Run(() =>
          {
            OrderActions.SaveJob(locomotive, _jobName ?? name);
            _jobName = null;
          }))
            .Disable(orders.Steps.Count == 0)
            .Tooltip("Save", orders.Steps.Count == 0 ? "Add some orders first" : "Save these orders under this name. A saved job with the same name is replaced.");
        });
        section.AddLabel("<alpha=#99>Saved jobs are kept outside the save, so any engine in any game can run them. Orders that couple to a car only work where that car is: for jobs you run again, use Pick up, Leave cars and Couple back.");
      });
      var jobs = SavedJobs.All();
      builder.AddSection("Saved jobs", section =>
      {
        section.ConfigureLayout(RowSpacing);
        if (jobs.Count == 0)
        {
          section.AddLabelEmptyState("No saved jobs yet");
          return;
        }
        section.VScrollView(list =>
        {
          list.ConfigureLayout(RowSpacing);
          foreach (var job in jobs)
          {
            var jobName = job.Name;
            KeepHeights(list.HStack(row =>
            {
              row.ConfigureLayout(RowButtonSpacing);
              var label = row.AddLabel(OrderActions.JobLine(job));
              label.FlexibleWidth();
              label.Tooltip(job.Name, OrderActions.JobTooltip(job));
              row.AddButtonCompact("Load", () => OrderActions.Run(() =>
              {
                if (OrderActions.LoadJob(locomotive, jobName, true)) ShowOrders();
              }))
                .Disable(orders.Running)
                .Tooltip("Load", orders.Running ? "Pause the orders first" : $"Replace {locomotive.DisplayName}'s orders with this job");
              row.AddButtonCompact("Add", () => OrderActions.Run(() =>
              {
                if (OrderActions.LoadJob(locomotive, jobName, false)) ShowOrders();
              }))
                .Tooltip("Add", $"Add this job to the end of {locomotive.DisplayName}'s orders");
              IconButton(row, icons, Icons.TrashName, "Delete", "Delete", "Delete this saved job", () => OrderActions.DeleteJob(jobName));
            }));
          }
        });
      });
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't show the saved jobs");
      builder.AddLabel("Couldn't show the saved jobs, see the Message Log.");
    }
  }

  private void ShowOrders()
  {
    _tab.Value = OrdersTab;
    RebuildPanel();
  }

  private static string Cars(int count) => count == 1 ? "1 car" : $"{count} cars";

  private static ForecastRun SafeForecast(BaseLocomotive locomotive, OrderList orders)
  {
    try
    {
      return Forecast.Run(locomotive, orders);
    }
    catch (Exception exception)
    {
      TrainOrdersMod.Log?.Error(exception, "Couldn't forecast the train");
      return new ForecastRun();
    }
  }

  private void AddHover(RectTransform row, int index)
  {
    if (row == null) return;
    var target = row.gameObject.AddComponent<Image>();
    target.color = new Color(0f, 0f, 0f, 0f);
    target.raycastTarget = true;
    var hover = row.gameObject.AddComponent<RowHover>();
    hover.Enter = () =>
    {
      var steps = _locomotive != null ? OrderList.Read(_locomotive).Steps : null;
      var track = steps != null && index >= 0 && index < steps.Count ? Ghosts.TurntableTrack(steps[index]) : null;
      if (track != null) Ghosts.ShowTrack(this, track);
      else if (_forecast != null && _forecast.States.TryGetValue(index, out var state)) Ghosts.Show(this, state);
    };
    hover.Exit = () => Ghosts.Clear(this);
  }

  private static void AddForecast(UIPanelBuilder entry, ForecastResult result)
  {
    entry.HStack(line =>
    {
      line.ConfigureLayout(0f);
      line.Spacer(SmartIndent);
      var rect = line.Spacer();
      var layout = rect.GetComponent<LayoutElement>();
      if (layout != null)
      {
        layout.minHeight = ConsistStrip.Height;
        layout.preferredHeight = ConsistStrip.Height;
        layout.flexibleHeight = 0f;
      }
      ConsistStrip.Add(rect, result);
      rect.Tooltip("Train after this order", ConsistStrip.TooltipText(result));
    });
  }

  private static void MenuButton(UIPanelBuilder builder, bool icons, string icon, string text, string title, string tooltip, Func<IReadOnlyList<MenuItem>> items)
  {
    RectTransform anchor = null;
    Action open = () => OrderActions.Run(() =>
    {
      _menu?.Dismiss();
      _menu = anchor != null ? PopupMenu.PresentUnder(anchor, items(), MenuOwner) : null;
    });
    anchor = (icons ? builder.AddIconButton(icon, open) : builder.AddButtonCompact(text, open)).Tooltip(title, tooltip).RectTransform;
  }

  private static IConfigurableElement IconButton(UIPanelBuilder builder, bool icons, string icon, string text, string title, string tooltip, Action action)
  {
    Action run = () => OrderActions.Run(action);
    var element = icons ? builder.AddIconButton(icon, run) : builder.AddButtonCompact(text, run);
    if (icons)
    {
      var size = element.RectTransform.rect.height;
      if (size > 0f) element.RectTransform.Width(size).Height(size);
    }
    return element.Tooltip(title, tooltip);
  }

  private static void KeepHeights(RectTransform stack)
  {
    var group = stack != null ? stack.GetComponent<HorizontalLayoutGroup>() : null;
    if (group == null) return;
    group.childForceExpandHeight = false;
    group.childAlignment = TextAnchor.UpperLeft;
  }

  internal static string EngineerStatus(BaseLocomotive locomotive)
  {
    if (locomotive == null) return "";
    var persistence = new AutoEngineerPersistence(locomotive.KeyValueObject);
    var status = persistence.PlannerStatus;
    return string.IsNullOrEmpty(status) ? persistence.Orders.Mode.ToString() : status;
  }
}
