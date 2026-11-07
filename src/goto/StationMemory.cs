using System;
using System.Collections.Generic;
using System.Linq;
using Game;

namespace QbanRambo.Goto;

internal static class StationMemory
{
  private const string Key = "qbanrambo.goto.station-jobs";

  internal static Dictionary<string, Record> Load()
  {
    var records = new Dictionary<string, Record>();
    var text = GlobalPreferences.GetString(Key, "") ?? "";
    foreach (var line in text.Split('\n'))
    {
      var parts = line.Split('\t');
      if (parts.Length < 3 || parts[0].Length == 0) continue;
      records[parts[0]] = new Record(parts[0], parts[1], parts[2]);
    }
    return records;
  }

  internal static void Remember(string locomotive, string stop, string location)
  {
    try
    {
      var records = Load();
      records[locomotive] = new Record(locomotive, stop, location ?? "");
      Save(records);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Couldn't remember the station stop: {exception.Message}");
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
      GotoMenu.Log?.Warning($"Couldn't forget the station stop: {exception.Message}");
    }
  }

  private static void Save(Dictionary<string, Record> records)
  {
    var text = string.Join("\n", records.Values.Select(record => string.Join("\t", record.Locomotive, record.Stop, record.Location)));
    if (text.Length == 0) GlobalPreferences.DeleteKey(Key);
    else GlobalPreferences.SetString(Key, text);
    GlobalPreferences.Flush();
  }

  internal sealed class Record
  {
    public Record(string locomotive, string stop, string location)
    {
      Locomotive = locomotive;
      Stop = stop;
      Location = location;
    }

    public string Locomotive { get; }
    public string Stop { get; }
    public string Location { get; }
  }
}
