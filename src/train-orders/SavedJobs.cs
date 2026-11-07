using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using KeyValue.Runtime;
using Newtonsoft.Json.Linq;
using KvType = KeyValue.Runtime.ValueType;

namespace QbanRambo.TrainOrders;

internal sealed class SavedJob
{
  public SavedJob(string name, bool repeat, bool passSignals, List<OrderStep> steps)
  {
    Name = name;
    Repeat = repeat;
    PassSignals = passSignals;
    Steps = steps;
  }

  public string Name { get; }
  public bool Repeat { get; }
  public bool PassSignals { get; }
  public List<OrderStep> Steps { get; }

  public bool NamesCars => Steps.Any(step => step.IsCouple || (step.Kind == StepKind.Move && step.Waypoint?.CoupleToCarId != null));
}

internal static class SavedJobs
{
  internal const int NameLimit = 40;

  private const string Key = "qbanrambo.train-orders.saved";
  private const string NameKey = "name";
  private const string RepeatKey = "repeat";
  private const string PassKey = "pass";
  private const string StepsKey = "steps";

  private static string _raw;
  private static List<SavedJob> _jobs = new();
  private static bool _logged;

  internal static IReadOnlyList<SavedJob> All()
  {
    var raw = GlobalPreferences.GetString(Key, "") ?? "";
    if (raw == _raw) return _jobs;
    _raw = raw;
    _jobs = Parse(raw);
    return _jobs;
  }

  internal static SavedJob Find(string name) => All().FirstOrDefault(job => Same(job.Name, name));

  internal static bool Save(SavedJob job)
  {
    var jobs = All().ToList();
    var index = jobs.FindIndex(existing => Same(existing.Name, job.Name));
    if (index >= 0) jobs[index] = job;
    else jobs.Add(job);
    Write(jobs);
    return index >= 0;
  }

  internal static void Delete(string name)
  {
    Write(All().Where(job => !Same(job.Name, name)).ToList());
  }

  private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

  private static void Write(List<SavedJob> jobs)
  {
    var array = new JArray(jobs.Select(job => new JObject
    {
      [NameKey] = job.Name,
      [RepeatKey] = job.Repeat,
      [PassKey] = job.PassSignals,
      [StepsKey] = new JArray(job.Steps.Select(step => ToJson(step.ToValue())))
    }));
    var raw = array.ToString(Newtonsoft.Json.Formatting.None);
    GlobalPreferences.SetString(Key, raw);
    GlobalPreferences.Flush();
    _raw = raw;
    _jobs = jobs;
  }

  private static List<SavedJob> Parse(string raw)
  {
    var jobs = new List<SavedJob>();
    if (string.IsNullOrWhiteSpace(raw)) return jobs;
    try
    {
      foreach (var token in JArray.Parse(raw).OfType<JObject>())
      {
        var name = token.Value<string>(NameKey);
        if (string.IsNullOrWhiteSpace(name)) continue;
        var steps = (token[StepsKey] as JArray ?? new JArray())
          .Select(step => OrderStep.FromValue(FromJson(step)))
          .Where(step => step != null)
          .ToList();
        jobs.Add(new SavedJob(name, token.Value<bool?>(RepeatKey) ?? false, token.Value<bool?>(PassKey) ?? false, steps));
      }
    }
    catch (Exception exception)
    {
      if (!_logged) TrainOrdersMod.Log?.Warning($"Couldn't read the saved jobs: {exception.Message}");
      _logged = true;
    }
    return jobs;
  }

  private static JToken ToJson(Value value)
  {
    switch (value.Type)
    {
      case KvType.Bool:
        return new JValue(value.BoolValue);
      case KvType.Int:
        return new JValue(value.IntValue);
      case KvType.Float:
        return new JValue(value.FloatValue);
      case KvType.String:
        return new JValue(value.StringValue);
      case KvType.Array:
        return new JArray(value.ArrayValue.Select(ToJson));
      case KvType.Dictionary:
        var dictionary = new JObject();
        foreach (var pair in value.DictionaryValue) dictionary[pair.Key] = ToJson(pair.Value);
        return dictionary;
      default:
        return JValue.CreateNull();
    }
  }

  private static Value FromJson(JToken token)
  {
    switch (token?.Type)
    {
      case JTokenType.Boolean:
        return Value.Bool(token.Value<bool>());
      case JTokenType.Integer:
        return Value.Int(token.Value<int>());
      case JTokenType.Float:
        return Value.Float(token.Value<float>());
      case JTokenType.String:
        return Value.String(token.Value<string>());
      case JTokenType.Array:
        return Value.Array(token.Select(FromJson).ToList());
      case JTokenType.Object:
        return Value.Dictionary(((JObject)token).Properties().ToDictionary(property => property.Name, property => FromJson(property.Value)));
      default:
        return Value.Null();
    }
  }
}
