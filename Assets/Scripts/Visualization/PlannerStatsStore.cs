using System.Collections.Generic;
using System;
using UnityEngine;

public static class PlannerStatsStore
{
    public class Entry
    {
        public int    successCount;
        public int    failCount;
        public int    experimentFailCount;
        public int    completedRunCount;
        public double totalTimeMs;
        public double totalTraveledM;
        public long   totalNodes;

        public int    TotalCount => successCount + failCount;

        public double AvgTimeMs  => successCount > 0 ? totalTimeMs / successCount : 0;
        public double AvgTraveledM => completedRunCount > 0 ? totalTraveledM / completedRunCount : 0;
        public long   AvgNodes   => completedRunCount > 0 ? totalNodes  / completedRunCount : 0;
    }

    public static readonly Dictionary<string, Entry> Data = new();
    public static PlannerRunRecord LastRecord;

    public static bool SuppressNextRecord { get; set; }

    public static string ActiveAlgorithm => activeRun.HasValue ? activeRun.Value.Algorithm : null;

    public static void RecordExperimentFailure(string algorithm)
    {
        if (!string.IsNullOrEmpty(algorithm))
            GetOrCreateEntry(algorithm).experimentFailCount++;
        CancelActiveRun();
        OnUpdated?.Invoke();
    }

    public static event System.Action OnUpdated;

    struct ActiveRunState
    {
        public string Algorithm;
        public Vector3 GoalUnity;
        public float StartTime;
        public double StartUnixSeconds;
        public int Sequence;
        public Vector3 LastRobotPosition;
        public float TraveledDistance;
    }

    static ActiveRunState? activeRun;
    static readonly List<PlannerRunRecord> pendingRecords = new();

    public static bool HasActiveRun => activeRun.HasValue;
    public static Vector3 ActiveGoalUnity => activeRun.HasValue ? activeRun.Value.GoalUnity : Vector3.zero;
    public static float ActiveRunSeconds => activeRun.HasValue ? Time.realtimeSinceStartup - activeRun.Value.StartTime : 0f;
    public static double ActiveRunStartUnixSeconds => activeRun.HasValue ? activeRun.Value.StartUnixSeconds : 0.0;
    public static int ActiveRunSequence => activeRun.HasValue ? activeRun.Value.Sequence : 0;
    public static bool HasPendingRecord => pendingRecords.Count > 0;
    static int nextRunSequence;

    public static void BeginRun(string algorithm, Vector3 goalUnity, Vector3 robotPosition)
    {
        SuppressNextRecord = false;
        pendingRecords.Clear();
        activeRun = new ActiveRunState
        {
            Algorithm = algorithm,
            GoalUnity = goalUnity,
            StartTime = Time.realtimeSinceStartup,
            StartUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
            Sequence = ++nextRunSequence,
            LastRobotPosition = robotPosition,
            TraveledDistance = 0f,
        };
        OnUpdated?.Invoke();
    }

    public static void UpdateRobotPosition(Vector3 pos)
    {
        if (!activeRun.HasValue) return;
        var run = activeRun.Value;
        float delta = Vector3.Distance(run.LastRobotPosition, pos);
        run.TraveledDistance += delta;
        run.LastRobotPosition = pos;
        activeRun = run;
    }

    public static void Record(PlannerRunRecord r)
    {
        if (SuppressNextRecord)
        {
            SuppressNextRecord = false;
            return;
        }

        if (!activeRun.HasValue)
            return;

        ActiveRunState run = activeRun.Value;
        r.Algorithm = string.IsNullOrEmpty(run.Algorithm) ? r.Algorithm : run.Algorithm;
        pendingRecords.Add(r);
    }

    public static void CompleteActiveRun()
    {
        if (!activeRun.HasValue) return;

        ActiveRunState run = activeRun.Value;
        List<PlannerRunRecord> completedRecords = new(pendingRecords);
        float traveled = run.TraveledDistance;
        activeRun = null;
        pendingRecords.Clear();

        if (completedRecords.Count == 0)
        {
            Debug.LogWarning("PlannerStatsStore: goal succeeded but no planner stats were received; no dashboard record committed");
            OnUpdated?.Invoke();
            return;
        }

        PlannerRunRecord firstPlan = null;
        PlannerRunRecord lastCommitted = null;
        foreach (PlannerRunRecord record in completedRecords)
        {
            record.Algorithm = string.IsNullOrEmpty(run.Algorithm)
                ? record.Algorithm
                : run.Algorithm;
            CommitCompletedRecord(record);

            if (record.PathFound)
            {
                firstPlan ??= record;
                lastCommitted = record;
            }
        }

        CommitRunMetrics(run.Algorithm, traveled, firstPlan);
        LastRecord = lastCommitted;
        OnUpdated?.Invoke();
    }

    static Entry GetOrCreateEntry(string algorithm)
    {
        if (string.IsNullOrEmpty(algorithm))
            algorithm = "Unknown";

        if (!Data.TryGetValue(algorithm, out var e))
        {
            e = new Entry();
            Data[algorithm] = e;
        }

        return e;
    }

    static void CommitCompletedRecord(PlannerRunRecord r)
    {
        Entry e = GetOrCreateEntry(r.Algorithm);

        if (r.PathFound)
        {
            e.successCount++;
            e.totalTimeMs += r.PlanTimeMs;
        }
        else
        {
            e.failCount++;
        }
    }

    static void CommitRunMetrics(string algorithm, float traveled, PlannerRunRecord firstPlan)
    {
        Entry e = GetOrCreateEntry(algorithm);

        e.completedRunCount++;
        e.totalTraveledM += traveled;

        if (firstPlan != null && firstPlan.PathFound)
        {
            e.totalNodes += firstPlan.NodesExpanded;
        }
    }

    public static void CancelActiveRun()
    {
        activeRun = null;
        pendingRecords.Clear();
        OnUpdated?.Invoke();
    }

    public static void ResetRuntimeState()
    {
        activeRun = null;
        pendingRecords.Clear();
        SuppressNextRecord = false;
        OnUpdated?.Invoke();
    }

    public static void Clear()
    {
        Data.Clear();
        LastRecord = null;
        activeRun = null;
        pendingRecords.Clear();
        OnUpdated?.Invoke();
    }
}

public class PlannerRunRecord
{
    public string Algorithm;
    public double PlanTimeMs;
    public double PathLengthM;
    public int    NodesExpanded;
    public bool   PathFound;
}
