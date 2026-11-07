using System.Linq;
using Track;

namespace QbanRambo.TrainOrders;

internal static class TrackWalk
{
  private const float SwitchClearance = 15f;
  private const float EndClearance = 2f;
  private const int MaxSegments = 200;

  internal static Location? Ahead(Location start, bool ahead, float distance, bool stopAtSwitch)
  {
    var graph = Graph.Shared;
    if (graph == null || distance < 1f) return null;
    var location = ahead ? start : start.Flipped();
    var segment = location.segment;
    if (segment == null) return null;
    var travelled = location.DistanceUntilEnd();
    if (travelled >= distance) return location.Moving(distance);
    var node = segment.NodeForEnd(location.EndIsA ? TrackSegment.End.B : TrackSegment.End.A);
    for (var i = 0; i < MaxSegments && node != null; i++)
    {
      TrackSegment next;
      if (graph.IsSwitch(node))
      {
        if (stopAtSwitch || !graph.DecodeSwitchAt(node, out var enter, out var normal, out _)) return Short(start, ahead, travelled - SwitchClearance, stopAtSwitch);
        next = segment == enter ? normal : enter;
      }
      else
      {
        var others = graph.SegmentsConnectedTo(node).Where(other => other != segment).ToList();
        next = others.Count == 1 ? others[0] : null;
      }
      if (next == null || !next.Available || next.ConstructionState != TrackConstructionState.Complete) return Short(start, ahead, travelled - EndClearance, stopAtSwitch);
      var length = next.GetLength();
      var end = next.NodeForEnd(TrackSegment.End.A) == node ? TrackSegment.End.A : TrackSegment.End.B;
      if (travelled + length >= distance) return new Location(next, distance - travelled, end);
      travelled += length;
      segment = next;
      node = next.GetOtherNode(node);
    }
    return Short(start, ahead, travelled - EndClearance, stopAtSwitch);
  }

  private static Location? Short(Location start, bool ahead, float distance, bool stopAtSwitch) => distance < 1f ? null : Ahead(start, ahead, distance, stopAtSwitch);
}
