using System.Collections.Concurrent;
using System.Globalization;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;

// ROS 端输出的是固定字段 JSON，用轻量解析保持 Unity 侧依赖最少
public class PlannerStatsReceiver : MonoBehaviour
{
    public const string StatsTopic = "/planner_stats";
    static readonly ConcurrentQueue<PlannerRunRecord> PendingRecords = new();

    void Start()
    {
        while (PendingRecords.TryDequeue(out _)) { }
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>(StatsTopic, OnStats);
        Debug.Log("PlannerStatsReceiver: 已订阅 " + StatsTopic);
    }

    void Update()
    {
        FlushPendingRecords();
    }

    public static void FlushPendingRecords()
    {
        while (PendingRecords.TryDequeue(out var r))
            PlannerStatsStore.Record(r);
    }

    static void OnStats(StringMsg msg)
    {
        var r = ParseJson(msg.data);
        if (r != null) PendingRecords.Enqueue(r);
    }

    static PlannerRunRecord ParseJson(string json)
    {
        try
        {
            return new PlannerRunRecord
            {
                Algorithm     = ExtractString(json, "algorithm"),
                PlanTimeMs    = ExtractDouble(json, "plan_time_ms"),
                PathLengthM   = ExtractDouble(json, "path_length_m"),
                NodesExpanded = (int)ExtractDouble(json, "nodes_expanded"),
                RunElapsedMs  = ExtractDouble(json, "run_time_ms"),
                CollisionCount = (int)ExtractDouble(json, "collision_count"),
                PathFound     = json.Contains("\"path_found\":true"),
            };
        }
        catch
        {
            Debug.LogWarning("PlannerStatsReceiver: JSON 解析失败: " + json);
            return null;
        }
    }

    static string ExtractString(string json, string key)
    {
        string k = $"\"{key}\":\"";
        int i = json.IndexOf(k, System.StringComparison.Ordinal);
        if (i < 0) return "";
        i += k.Length;
        int j = json.IndexOf('"', i);
        return j < 0 ? "" : json.Substring(i, j - i);
    }

    static double ExtractDouble(string json, string key)
    {
        string k = $"\"{key}\":";
        int i = json.IndexOf(k, System.StringComparison.Ordinal);
        if (i < 0) return 0;
        i += k.Length;
        int j = i;
        while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '.' || json[j] == '-')) j++;
        if (j == i) return 0;
        return double.Parse(json.Substring(i, j - i), CultureInfo.InvariantCulture);
    }
}
