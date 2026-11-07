using System;
using System.Collections.Generic;
using System.Linq;
using Game;

namespace QbanRambo.Goto;

internal static class RefuelMemory
{
  private const string Key = "qbanrambo.goto.refuel-jobs";

  internal static Dictionary<string, Record> Load()
  {
    var records = new Dictionary<string, Record>();
    var text = GlobalPreferences.GetString(Key, "") ?? "";
    foreach (var line in text.Split('\n'))
    {
      var parts = line.Split('\t');
      if (parts.Length < 4 || parts[0].Length == 0) continue;
      records[parts[0]] = new Record(parts[0], parts[1], parts[2], parts[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList());
    }
    return records;
  }

  internal static void Remember(string locomotive, string phase, string location, IEnumerable<string> stops)
  {
    try
    {
      var records = Load();
      records[locomotive] = new Record(locomotive, phase, location ?? "", stops.ToList());
      Save(records);
    }
    catch (Exception exception)
    {
      GotoMenu.Log?.Warning($"Couldn't remember the refuelling: {exception.Message}");
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
      GotoMenu.Log?.Warning($"Couldn't forget the refuelling: {exception.Message}");
    }
  }

  private static void Save(Dictionary<string, Record> records)
  {
    var text = string.Join("\n", records.Values.Select(record => string.Join("\t", record.Locomotive, record.Phase, record.Location, string.Join(",", record.Stops))));
    if (text.Length == 0) GlobalPreferences.DeleteKey(Key);
    else GlobalPreferences.SetString(Key, text);
    GlobalPreferences.Flush();
  }

  internal sealed class Record
  {
    public Record(string locomotive, string phase, string location, List<string> stops)
    {
      Locomotive = locomotive;
      Phase = phase;
      Location = location;
      Stops = stops;
    }

    public string Locomotive { get; }
    public string Phase { get; }
    public string Location { get; }
    public List<string> Stops { get; }
  }
}
