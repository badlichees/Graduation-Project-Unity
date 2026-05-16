using System.IO;
using UnityEditor;
using UnityEngine;

public class PlannerDashboardWindow : EditorWindow
{
    static readonly string[] Algorithms =
        { "Astar", "Dijkstra", "Greedy", "NavFn", "RRTStar", "DLite", "JPS", "WAStar" };

    static readonly Color[] AlgoColors =
    {
        new Color(0.20f, 0.60f, 1.00f),
        new Color(0.20f, 0.90f, 0.30f),
        new Color(1.00f, 0.55f, 0.10f),
        new Color(0.85f, 0.85f, 0.20f),
        new Color(0.85f, 0.20f, 0.85f),
        new Color(0.20f, 0.90f, 0.90f),
        new Color(1.00f, 0.25f, 0.25f),
        new Color(1.00f, 0.75f, 0.15f),
    };

    [MenuItem("Tools/算法性能面板 %#p")]
    static void Open() => GetWindow<PlannerDashboardWindow>("算法性能面板");

    void OnEnable()
    {
        EditorApplication.update += Repaint;
        PlannerStatsStore.OnUpdated += Repaint;
    }

    void OnDisable()
    {
        EditorApplication.update -= Repaint;
        PlannerStatsStore.OnUpdated -= Repaint;
    }

    Vector2 scroll;

    void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(4);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        DrawTable();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        DrawLastRecord();
        EditorGUILayout.Space(4);
        DrawButtons();
    }

    void DrawHeader()
    {
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("路径规划算法性能对比", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            string status = EditorApplication.isPlaying
                ? "<color=#44ff88>● Play</color>"
                : "<color=#888888>○ 未运行</color>";
            GUILayout.Label(status, new GUIStyle(EditorStyles.label) { richText = true });
        }
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.4f, 0.4f, 0.4f));
    }

void DrawTable()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            ColHeader("算法", 70);
            ColHeader("规划次数", 52);
            ColHeader("实验失败", 58);
            ColHeader("耗时 avg(ms)", 100);
            ColHeader("行驶距离 avg (m)", 108);
            ColHeader("节点 avg", 80);
        }

        var data = PlannerStatsStore.Data;
        for (int i = 0; i < Algorithms.Length; ++i)
        {
            string algo = Algorithms[i];
            Color dot   = AlgoColors[i];

            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(21));
            EditorGUI.DrawRect(rowRect, i % 2 == 0
                ? new Color(0.22f, 0.22f, 0.22f)
                : new Color(0.25f, 0.25f, 0.25f));

            var dotRect = GUILayoutUtility.GetRect(10, 21, GUILayout.Width(10));
            EditorGUI.DrawRect(new Rect(dotRect.x + 1, dotRect.y + 5, 8, 11), dot);
            Cell(algo, 60, TextAnchor.MiddleLeft);

            if (data.TryGetValue(algo, out var e) && e.TotalCount > 0)
            {
                string nodesStr = algo == "NavFn" ? "N/A" : e.AvgNodes.ToString();
                Cell(e.TotalCount.ToString(), 52);
                Cell(e.experimentFailCount.ToString(), 58);
                Cell($"{e.AvgTimeMs:F2}",    100);
                Cell($"{e.AvgTraveledM:F2}", 108);
                Cell(nodesStr,                80);
            }
            else
            {
                Cell("—", 52); Cell("—", 58); Cell("—", 100); Cell("—", 108); Cell("—", 80);
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    void DrawLastRecord()
    {
        var r = PlannerStatsStore.LastRecord;
        if (r == null) return;

        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.4f, 0.4f, 0.4f));
        EditorGUILayout.Space(2);

        string found = r.PathFound
            ? "<color=#44ff88>✓ 成功</color>"
            : "<color=#ff4444>✗ 失败</color>";
        string nodes = r.Algorithm == "NavFn" ? "N/A" : r.NodesExpanded.ToString();

        GUILayout.Label(
            $"<b>最近：{r.Algorithm}</b>  " +
            $"耗时 {r.PlanTimeMs:F1} ms  路径 {r.PathLengthM:F2} m  节点 {nodes}  {found}",
            new GUIStyle(EditorStyles.label) { richText = true });
    }

    void DrawButtons()
    {
        // Reset 依赖场景运行时单例，编辑模式下只显示不可用状态
        using (new EditorGUILayout.HorizontalScope())
        {
            bool canReset = EditorApplication.isPlaying && RobotResetController.Instance != null;
            using (new EditorGUI.DisabledScope(!canReset))
            {
                if (GUILayout.Button(
                    canReset ? "▶  重置机器人位姿  [R]" : "重置机器人位姿（Play Mode 可用）",
                    GUILayout.Height(30)))
                {
                    RobotResetController.Instance.ResetPose();
                }
            }
        }

        EditorGUILayout.Space(2);

        using (new EditorGUILayout.HorizontalScope())
        {
            int total = 0;
            foreach (var e in PlannerStatsStore.Data.Values) total += e.TotalCount;
            GUILayout.Label($"累计 {total} 次规划",
                new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft },
                GUILayout.Width(100));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("清空数据", GUILayout.Height(24), GUILayout.Width(80)))
            {
                PlannerStatsStore.Clear();
                PlannerStatsStorePersistence.ClearSession();
            }

            if (GUILayout.Button("导出 CSV", GUILayout.Height(24), GUILayout.Width(80)))
                ExportCSV();
        }
    }

void ExportCSV()
    {
        string path = EditorUtility.SaveFilePanel("导出 CSV", "", "planner_stats.csv", "csv");
        if (string.IsNullOrEmpty(path)) return;

        using var w = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        w.WriteLine("算法,总次数,成功次数,失败次数,实验失败次数,平均耗时(ms),平均行驶距离(m),平均展开节点数");

        foreach (string algo in Algorithms)
        {
            if (!PlannerStatsStore.Data.TryGetValue(algo, out var e))
            {
                w.WriteLine($"{algo},0,0,0,0,0,0,0");
                continue;
            }
            string nodes = algo == "NavFn" ? "N/A" : e.AvgNodes.ToString();
            w.WriteLine($"{algo},{e.TotalCount},{e.successCount},{e.failCount},{e.experimentFailCount}," +
                        $"{e.AvgTimeMs:F1},{e.AvgTraveledM:F2},{nodes}");
        }

        Debug.Log($"PlannerDashboard: CSV 已保存至 {path}");
        EditorUtility.RevealInFinder(path);
    }

    static GUIStyle cellStyle;

    static void Cell(string text, int width, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        cellStyle ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
        cellStyle.alignment = anchor;
        GUILayout.Label(text, cellStyle, GUILayout.Width(width));
    }

    static void ColHeader(string text, int width)
    {
        GUILayout.Label(text,
            new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleCenter },
            GUILayout.Width(width));
    }
}
