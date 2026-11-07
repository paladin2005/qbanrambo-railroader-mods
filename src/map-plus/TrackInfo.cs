using System.Collections.Generic;
using System.Linq;
using Game.Mow;
using Track;
using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class TrackInfo
{
  private static readonly int[] MaxSpeeds = { 10, 25, 40, 60, 80, 120 };

  private static readonly Color[] Colors =
  {
    new(1f, 0.32f, 0.5f),
    new(0.78f, 0.45f, 1f),
    new(0.35f, 0.55f, 1f),
    new(0.2f, 0.8f, 0.92f),
    new(0.3f, 0.86f, 0.42f)
  };

  internal static bool IsBuilt(TrackSegment segment) => segment.Available && segment.ConstructionState == TrackConstructionState.Complete;

  internal static int ConditionPercent(TrackConditionController controller, TrackSegment segment)
  {
    var condition = controller.GetCondition(segment);
    var built = controller.GetGradingClass(segment);
    if (built < 1 || built > 5) return Mathf.RoundToInt(condition * 100f);
    var target = built * 0.2f;
    return target - condition <= 0.001f ? 100 : Mathf.FloorToInt(Mathf.Min(condition / target, 1f) * 100f);
  }

  internal static int ClassOf(TrackSegment segment) => Mathf.Clamp(segment.PostedTrackClass, 1, Colors.Length);

  internal static Color ColorFor(TrackSegment segment) => Colors[ClassOf(segment) - 1];

  private static int MaxSpeedMph(int trackClass) => MaxSpeeds[Mathf.Clamp(trackClass, 1, MaxSpeeds.Length) - 1];

  private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

  internal static string Legend()
  {
    return "Class " + string.Join("  ", Enumerable.Range(1, Colors.Length).Select(trackClass => $"<color=#{Hex(Colors[trackClass - 1])}><b>{trackClass}</b></color>"));
  }

  internal static int Signature()
  {
    var graph = Graph.Shared;
    if (graph == null) return 0;
    unchecked
    {
      var hash = 17;
      foreach (var segment in graph.AllSegments)
      {
        if (segment == null || !IsBuilt(segment)) continue;
        hash = hash * 31 + segment.PostedTrackClass;
      }
      return hash;
    }
  }

  internal static List<string> Describe(TrackSegment segment, Location location)
  {
    var lines = new List<string>();
    if (!IsBuilt(segment))
    {
      lines.Add(segment.Available ? "Not built yet" : "Not available");
      return lines;
    }
    var trackClass = ClassOf(segment);
    lines.Add($"<color=#{Hex(Colors[trackClass - 1])}>Class {trackClass}</color>, up to {MaxSpeedMph(trackClass)} mph");
    lines.Add($"Speed limit {segment.EffectiveSpeedLimitMph} mph");
    var controller = TrackConditionController.Shared;
    if (controller != null)
    {
      lines.Add($"Condition {ConditionPercent(controller, segment)}%");
      var built = controller.GetGradingClass(segment);
      if (built > 0) lines.Add($"Built for class {built}");
    }
    var graph = Graph.Shared;
    if (graph != null) lines.Add($"Grade {Mathf.Abs(graph.GradeAtLocation(location)):0.0}%");
    if ((segment.flags & TrackSegment.Flags.Bridge) != 0) lines.Add((segment.flags & TrackSegment.Flags.BridgeSupportsSteel) != 0 ? "Steel bridge" : "Bridge");
    if ((segment.flags & TrackSegment.Flags.Tunnel) != 0) lines.Add("Tunnel");
    return lines;
  }

  internal static string KindName(TrackSegment segment)
  {
    if (TrackPainter.IsMainline(segment)) return "Mainline";
    return (segment.flags & TrackSegment.Flags.Yard) != 0 ? "Yard track" : "Track";
  }

  internal static string Town(Location location)
  {
    var graph = Graph.Shared;
    var ops = Model.Ops.OpsController.Shared;
    if (graph == null || ops == null) return null;
    var area = ops.ClosestAreaForGamePosition(graph.GetPosition(location));
    return area != null && !string.IsNullOrEmpty(area.name) ? area.name : null;
  }
}
