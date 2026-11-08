using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Model;
using Model.AI;
using UI.Common;
using UI.EngineControls;
using UnityEngine;

namespace QbanRambo.Goto;

internal sealed class CoupleWatch : MonoBehaviour
{
  internal const string WatchedKey = "qbanrambo.couple-watched";

  private const float TickSeconds = 0.5f;
  private const float SettleSeconds = 2f;
  private const float OrderSettleSeconds = 2f;
  private const float TimeoutSeconds = 1800f;

  private static CoupleWatch _shared;

  private readonly Dictionary<string, Job> _jobs = new();
  private float _nextTick;

  internal static void Begin(BaseLocomotive locomotive, Car car, string locationString)
  {
    if (_shared == null) _shared = new GameObject("Goto Couple Watch").AddComponent<CoupleWatch>();
    _shared._jobs[locomotive.id] = new Job(locomotive, car, locationString, locomotive.EnumerateCoupled().Count());
  }

  internal static void Clear()
  {
    if (_shared != null)
    {
      _shared._jobs.Clear();
      Destroy(_shared.gameObject);
    }
    _shared = null;
  }

  private void OnDestroy()
  {
    if (_shared == this) _shared = null;
  }

  private void Update()
  {
    if (Time.unscaledTime < _nextTick) return;
    _nextTick = Time.unscaledTime + TickSeconds;
    foreach (var id in _jobs.Keys.ToList())
    {
      try
      {
        if (Check(_jobs[id])) _jobs.Remove(id);
      }
      catch (Exception exception)
      {
        GotoMenu.Log?.Error(exception, "Couldn't finish the coupling");
        _jobs.Remove(id);
      }
    }
    if (_jobs.Count == 0) Destroy(gameObject);
  }

  private static bool Check(Job job)
  {
    var locomotive = job.Locomotive;
    if (locomotive == null || job.Car == null || Time.time - job.Started > TimeoutSeconds) return true;
    if (!locomotive.EnumerateCoupled().Contains(job.Car))
    {
      if (Time.time - job.Started <= OrderSettleSeconds) return false;
      var orders = new AutoEngineerPersistence(locomotive.KeyValueObject).Orders;
      var current = orders.Waypoint?.LocationString;
      return orders.Mode != AutoEngineerMode.Waypoint || (current != null && current != job.LocationString);
    }
    if (!locomotive.IsStopped(SettleSeconds)) return false;
    Coupling.ConnectAir(locomotive);
    var added = locomotive.EnumerateCoupled().Count() - job.CarsBefore;
    var released = GotoSettings.ReleaseHandbrakes ? GotoMenu.ReleaseHandbrakes(locomotive) : 0;
    var cars = added == 1 ? "1 car" : $"{Mathf.Max(added, 1)} cars";
    var handbrakes = released == 0 ? "" : $", released {released} {(released == 1 ? "handbrake" : "handbrakes")}";
    Toast.Present($"{locomotive.DisplayName} coupled to {cars}, air connected{handbrakes}", ToastPosition.Bottom);
    return true;
  }

  private sealed class Job
  {
    public Job(BaseLocomotive locomotive, Car car, string locationString, int carsBefore)
    {
      Locomotive = locomotive;
      Car = car;
      LocationString = locationString;
      CarsBefore = carsBefore;
      Started = Time.time;
    }

    public BaseLocomotive Locomotive { get; }
    public Car Car { get; }
    public string LocationString { get; }
    public int CarsBefore { get; }
    public float Started { get; }
  }
}
