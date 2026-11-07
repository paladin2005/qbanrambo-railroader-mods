using System;
using System.Collections.Generic;
using System.Linq;
using Game;

namespace QbanRambo.Goto;

internal static class TurntableMemory
{
  private const string Key = "qbanrambo.goto.turntable-jobs";

  internal static Dictionary<string, Record> Load()
  {
    var records = new Dictionary<string, Record>();
    var text = GlobalPreferences.GetString(Key, "") ?? "";
    foreach (var line in text.Split('\n'))
    {
      var parts = line.Split('\t');
      if (parts.Length < 6 || parts[0].Length == 0 || parts[1].Length == 0) continue;
      if (!int.TryParse(parts[2], out var goal) || !int.TryParse(parts[3], out var phase) || !int.TryParse(parts[4], out var entry) || !int.TryParse(parts[5], out var stop)) continue;
      records[parts[0]] = new Record(parts[0], parts[1], goal, phase, entry, stop);
    }
    return records;
  }

  internal static void Remember(Record record)
  {
    try
    {
      var records = Load();
      records[record.Locomotive] = record;
      Save(records);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Couldn't remember the turntable move: {exception.Message}");
    }
  }

  internal static void Forget(string locomotive)
  {
    try
    {
      var records = Load();
      if (records.Remove(locomotive)) Save(records);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Couldn't forget the turntable move: {exception.Message}");
    }
  }

  private static void Save(Dictionary<string, Record> records)
  {
    var text = string.Join("\n", records.Values.Select(record => string.Join("\t", record.Locomotive, record.Turntable, record.Goal, record.Phase, record.Entry, record.Stop)));
    if (text.Length == 0) GlobalPreferences.DeleteKey(Key);
    else GlobalPreferences.SetString(Key, text);
    GlobalPreferences.Flush();
  }

  internal sealed class Record
  {
    public Record(string locomotive, string turntable, int goal, int phase, int entry, int stop)
    {
      Locomotive = locomotive;
      Turntable = turntable;
      Goal = goal;
      Phase = phase;
      Entry = entry;
      Stop = stop;
    }

    public string Locomotive { get; }
    public string Turntable { get; }
    public int Goal { get; }
    public int Phase { get; }
    public int Entry { get; }
    public int Stop { get; }
  }
}
