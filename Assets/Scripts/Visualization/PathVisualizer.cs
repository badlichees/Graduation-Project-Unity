using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using UnityEngine;

/// <summary>
/// 订阅 Nav2 /plan（nav_msgs/Path），用 LineRenderer 在 Unity 场景中画出路径。
/// 颜色随当前规划算法自动切换。
/// 挂到带有 LineRenderer 组件的 GameObject 上。
/// </summary>
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
    string currentAlgorithm = "Astar";

    // Reset() 在 Editor 里添加组件时立即执行，解决 Edit Mode 粉红色问题
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
        lr = GetComponent<LineRenderer>();
        lr.startWidth    = lineWidth;
        lr.endWidth      = lineWidth;
        lr.useWorldSpace = true;
        lr.positionCount = 0;

        // 运行时用实例化材质，避免多个 Renderer 共享同一材质时颜色互相干扰
        lr.material = lineMaterial != null ? lineMaterial : MakeLineMaterial();

        ROSConnection.GetOrCreateInstance().Subscribe<PathMsg>(planTopic, OnPlan);
        goalPublisher = FindFirstObjectByType<GoalPublisher>();
        Debug.Log("PathVisualizer: 已订阅 " + planTopic);
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
        if (msg.poses == null || msg.poses.Length == 0)
        {
            lr.positionCount = 0;
            return;
        }

        Color c = AlgoColors.TryGetValue(currentAlgorithm, out var col) ? col : Color.white;
        lr.startColor    = c;
        lr.endColor      = c;
        lr.material.color = c;  // Unlit/Color 需要此行；支持顶点色的 shader 忽略它
        lr.positionCount = msg.poses.Length;

        for (int i = 0; i < msg.poses.Length; ++i)
        {
            var p = msg.poses[i].pose.position;
            // ROS(x,y) → Unity(X,Z)：rosX→unityZ，rosY→-unityX
            lr.SetPosition(i, new Vector3(-(float)p.y, lineHeight, (float)p.x));
        }
    }

    public void ClearPath() => lr.positionCount = 0;
}
