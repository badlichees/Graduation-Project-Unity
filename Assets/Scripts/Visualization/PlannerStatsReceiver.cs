using System.Globalization;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;

/// <summary>
/// 订阅 /planner_stats（std_msgs/String JSON），解析后写入 PlannerStatsStore。
/// 挂到场景中任意 GameObject 上（推荐和 GoalPublisher 同一对象）。
/// </summary>
public class PlannerStatsReceiver : MonoBehaviour
{
    public const string StatsTopic = "/planner_stats";

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>(StatsTopic, OnStats);
        Debug.Log("PlannerStatsReceiver: 已订阅 " + StatsTopic);
    }

    static void OnStats(StringMsg msg)
    {
        var r = ParseJson(msg.data);
        if (r != null) PlannerStatsStore.Record(r);
    }

    // 手动解析固定格式 JSON，避免引入外部依赖
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
        return double.Parse(json.Substring(i, j - i), CultureInfo.InvariantCulture);
    }
}
