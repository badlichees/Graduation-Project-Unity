using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RobotSimulation.MapGeneration;
using UnityEngine;

// 路径颜色跟随当前算法选择，方便同一场景里肉眼对比规划结果
[RequireComponent(typeof(LineRenderer))]
public class PathVisualizer : MonoBehaviour
{
    [Header("ROS")]
    public string planTopic = "/plan";

    [Header("Visual")]
    [Range(0.01f, 0.2f)] public float lineWidth  = 0.05f;
    public float lineHeight = 0.03f;
    [Tooltip("留空则自动创建 Unlit/Color 材质")]
    public Material lineMaterial;
    public bool drawOnlyDuringActiveRun = true;

    static readonly Dictionary<string, Color> AlgoColors = new()
    {
        { "Astar",    new Color(0.20f, 0.60f, 1.00f) },
        { "Dijkstra", new Color(0.20f, 0.90f, 0.30f) },
        { "Greedy",   new Color(1.00f, 0.55f, 0.10f) },
        { "NavFn",    new Color(0.85f, 0.85f, 0.20f) },
        { "RRTStar",  new Color(0.85f, 0.20f, 0.85f) },
        { "DLite",    new Color(0.20f, 0.90f, 0.90f) },
        { "JPS",      new Color(1.00f, 0.25f, 0.25f) },
        { "WAStar",   new Color(1.00f, 0.75f, 0.15f) },
    };

    LineRenderer lr;
    GoalPublisher goalPublisher;
    MapGenerator mapGenerator;
    string currentAlgorithm = "Astar";

#if UNITY_EDITOR
    void Reset()
    {
        var renderer = GetComponent<LineRenderer>();
        if (renderer == null) return;
        renderer.sharedMaterial = MakeLineMaterial();
        renderer.startWidth = lineWidth;
        renderer.endWidth   = lineWidth;
        renderer.useWorldSpace = true;
    }
#endif

    void Start()
    {
        PlannerStatsStore.ResetRuntimeState();

        lr = GetComponent<LineRenderer>();
        lr.startWidth    = lineWidth;
        lr.endWidth      = lineWidth;
        lr.useWorldSpace = true;
        lr.positionCount = 0;

        // 运行时用实例化材质，避免多个 Renderer 共享同一材质时颜色互相干扰
        lr.material = lineMaterial != null ? lineMaterial : MakeLineMaterial();

        ROSConnection.GetOrCreateInstance().Subscribe<PathMsg>(planTopic, OnPlan);
        goalPublisher = FindFirstObjectByType<GoalPublisher>();
        mapGenerator = FindFirstObjectByType<MapGenerator>();
        if (mapGenerator != null)
            mapGenerator.OnMapGenerated += ClearPath;
        Debug.Log("PathVisualizer: 已订阅 " + planTopic);
    }

    void OnDestroy()
    {
        if (mapGenerator != null)
            mapGenerator.OnMapGenerated -= ClearPath;
    }

    static Material MakeLineMaterial()
    {
        var shader = Shader.Find("Unlit/Color")
                  ?? Shader.Find("Sprites/Default")
                  ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        return shader != null ? new Material(shader) { name = "PathLine (auto)" } : null;
    }

    void Update()
    {
        if (goalPublisher != null)
            currentAlgorithm = goalPublisher.ActiveAlgorithm;
    }

    void OnPlan(PathMsg msg)
    {
        if (drawOnlyDuringActiveRun && !PlannerStatsStore.HasActiveRun)
        {
            ClearPath();
            return;
        }

        if (msg.poses == null || msg.poses.Length == 0)
        {
            lr.positionCount = 0;
            return;
        }

        Color c = AlgoColors.TryGetValue(currentAlgorithm, out var col) ? col : Color.white;
        lr.startColor    = c;
        lr.endColor      = c;
        // Unlit/Color 需要材质色，支持顶点色的 shader 会忽略它
        lr.material.color = c;
        lr.positionCount = msg.poses.Length;

        for (int i = 0; i < msg.poses.Length; ++i)
        {
            var p = msg.poses[i].pose.position;
            // 与 goal/odom/map 的轴向映射保持一致
            lr.SetPosition(i, new Vector3(-(float)p.y, lineHeight, (float)p.x));
        }
    }

    public void ClearPath() => lr.positionCount = 0;
}
