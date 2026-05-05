using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System;
using RobotSimulation.MapGeneration;

public class GoalPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/goal_pose";
    public string frameId = "map";
    [Min(1)]
    public int publisherQueueSize = 10;

    [Header("Robot Reference")]
    public Transform robotTransform;

    [Header("Goal Input")]
    [Tooltip("目标点 Unity 世界系 (X, Z)，单位米")]
    public Vector2 targetPositionXZ = Vector2.zero;
    public KeyCode publishKey = KeyCode.Space;
    public bool allowMouseClick = false;

    [Header("Map Assistance")]
    public MapGenerator mapGenerator;
    [Min(0)] public int maxSnapSearchRadius = 8;
    [Min(0)] public int goalClearanceRadiusTiles = 1;

    [Header("Test")]
    public bool autoPublishTestGoal = false;
    public float testGoalDistance = 2.0f;
    [Min(0f)] public float autoPublishDelay = 2.0f;

    [Header("Algorithm Selector")]
    [Tooltip("与 Nav2 planner_server 中注册的插件 ID 对应")]
    public string[] availableAlgorithms = { "Astar", "Dijkstra", "Greedy", "NavFn", "RRTStar", "DLite", "JPS", "WAStar" };
    [Tooltip("当前选中算法的下标（运行时按 Tab 切换）")]
    public int selectedAlgorithmIndex = 0;

    public const string PlannerSelectorTopic = "/planner_selector_unity";

    [Header("Visual Marker")]
    [Range(0.05f, 0.5f)]
    public float markerRadius = 0.12f;

    private ROSConnection ros;
    private GameObject goalMarker;
    private bool markerReady = false;
    private bool debugGoalPublished = false;
    private Vector2 lastPreviewedXZ = new Vector2(float.NaN, float.NaN);
    private Vector3 currentGoalPoint;
    private bool hasGoalPoint = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(topicName, publisherQueueSize);
        ros.RegisterPublisher<StringMsg>(PlannerSelectorTopic, 1);
        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<MapGenerator>();

        CreateMarker();

        if (mapGenerator != null)
            mapGenerator.OnMapGenerated += RefreshTargetPreview;

        if (mapGenerator == null || mapGenerator.GeneratedObstacleMap != null)
            RefreshTargetPreview();

        InvokeRepeating(nameof(PublishPlannerSelection), 0f, 0.5f);

        Debug.Log($"GoalPublisher: 已启动，按 [{publishKey}] 发布目标，按 [Tab] 切换算法，当前：{ActiveAlgorithm}");
    }

    void Update()
    {
        HandleAutoTestGoal();

        if (targetPositionXZ != lastPreviewedXZ)
            RefreshTargetPreview();

        if (Input.GetKeyDown(KeyCode.Tab))
            CycleAlgorithm();

        if (Input.GetKeyDown(publishKey))
        {
            // 发布前重新计算，避免算法切换或上一轮测试留下旧的缓存目标点
            RefreshTargetPreview();
            if (hasGoalPoint)
                PublishGoalPoint(currentGoalPoint, "inspector");
        }

        if (!allowMouseClick || !Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        TryPublishGoal();
    }

    void OnDestroy()
    {
        if (mapGenerator != null)
            mapGenerator.OnMapGenerated -= RefreshTargetPreview;
        if (goalMarker != null)
            Destroy(goalMarker);
    }

    void RefreshTargetPreview()
    {
        Vector3 desired = new Vector3(targetPositionXZ.x, 0f, targetPositionXZ.y);
        currentGoalPoint = SnapGoalToNearestOpenCell(desired);
        MoveMarker(currentGoalPoint);
        lastPreviewedXZ = targetPositionXZ;
        hasGoalPoint = true;
    }

    void TryPublishGoal()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("GoalPublisher: 未找到 Main Camera");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Mathf.Abs(ray.direction.y) < 1e-6f) return;

        float t = -ray.origin.y / ray.direction.y;
        if (t < 0f) return;

        Vector3 hitPoint = new Vector3(
            ray.origin.x + t * ray.direction.x,
            0f,
            ray.origin.z + t * ray.direction.z
        );

        Vector3 goalPoint = SnapGoalToNearestOpenCell(hitPoint);

        // 鼠标点选也写回 Inspector 目标，便于后续切换算法后用 Space 重复同一目标
        currentGoalPoint = goalPoint;
        targetPositionXZ = new Vector2(goalPoint.x, goalPoint.z);
        lastPreviewedXZ = targetPositionXZ;
        hasGoalPoint = true;

        PublishGoalPoint(goalPoint, "mouse");
    }

    Vector3 SnapGoalToNearestOpenCell(Vector3 hitPoint)
    {
        if (mapGenerator == null || mapGenerator.GeneratedObstacleMap == null)
            return hitPoint;

        bool outsideMap = IsOutsideMapBounds(hitPoint, out Vector2 min, out Vector2 max);
        Coord clickedGrid = mapGenerator.WorldToGrid(hitPoint);
        if (!mapGenerator.TryFindNearestOpenTile(
            clickedGrid,
            out Coord snappedGrid,
            maxSnapSearchRadius,
            goalClearanceRadiusTiles))
        {
            Debug.LogWarning($"GoalPublisher: 未能在半径 {maxSnapSearchRadius} 内找到满足净空要求的可达格，使用原始点击点");
            return hitPoint;
        }

        Vector3 snappedWorld = mapGenerator.GridToWorld(snappedGrid);
        if (outsideMap)
        {
            Debug.LogWarning(
                $"GoalPublisher: 目标 Unity({hitPoint.x:F2}, {hitPoint.z:F2}) 超出地图范围 " +
                $"X[{min.x:F2}, {max.x:F2}], Z[{min.y:F2}, {max.y:F2}]，" +
                $"已使用最近可导航格 Unity({snappedWorld.x:F2}, {snappedWorld.z:F2})");
        }
        else if (snappedGrid != clickedGrid)
        {
            Debug.Log(
                $"GoalPublisher: 目标已吸附到最近空闲格 " +
                $"{clickedGrid} -> {snappedGrid}，净空半径={goalClearanceRadiusTiles}，" +
                $"Unity({snappedWorld.x:F2}, {snappedWorld.z:F2})");
        }
        return snappedWorld;
    }

    void PublishGoalPoint(Vector3 goalPoint, string sourceLabel)
    {
        MoveMarker(goalPoint);

        // Unity 地面是 XZ，ROS 地图是 XY；和 map/odom 发布端保持同一套轴向约定
        double rosX = goalPoint.z;
        double rosY = -goalPoint.x;
        double yawROS = ComputeGoalYawROS(goalPoint);

        var stamp = MakeStamp();
        var msg = new PoseStampedMsg
        {
            header = new HeaderMsg
            {
                stamp = stamp,
                frame_id = frameId
            },
            pose = new PoseMsg
            {
                position = new PointMsg { x = rosX, y = rosY, z = 0.0 },
                orientation = new QuaternionMsg
                {
                    x = 0.0,
                    y = 0.0,
                    z = Math.Sin(yawROS * 0.5),
                    w = Math.Cos(yawROS * 0.5)
                }
            }
        };

        ros.Publish(topicName, msg);

        Debug.Log(
            $"GoalPublisher: [{sourceLabel}] 目标已发布 " +
            $"Unity({goalPoint.x:F2}, {goalPoint.z:F2}) -> ROS({rosX:F2}, {rosY:F2}), " +
            $"yaw={yawROS * Mathf.Rad2Deg:F1}°");
    }

    public void PublishSilentGoal(Vector3 unityPos, Quaternion unityRot)
    {
        if (ros == null) return;
        // 重置机器人会触发一次“取消旧导航”的 goal，不应计入算法性能统计
        PlannerStatsStore.SuppressNextRecord = true;

        double rosX = unityPos.z;
        double rosY = -unityPos.x;

        float yawUnity = Mathf.Atan2(
            2f * (unityRot.w * unityRot.y + unityRot.x * unityRot.z),
            1f - 2f * (unityRot.y * unityRot.y + unityRot.z * unityRot.z));
        double yawROS = -yawUnity;

        ros.Publish(topicName, new PoseStampedMsg
        {
            header = new HeaderMsg { stamp = MakeStamp(), frame_id = frameId },
            pose   = new PoseMsg
            {
                position    = new PointMsg { x = rosX, y = rosY, z = 0.0 },
                orientation = new QuaternionMsg
                {
                    x = 0.0, y = 0.0,
                    z = Math.Sin(yawROS * 0.5),
                    w = Math.Cos(yawROS * 0.5)
                }
            }
        });
        Debug.Log($"GoalPublisher: 静默目标已发布至初始位置 ROS({rosX:F2},{rosY:F2})，Nav2 将取消当前导航");
    }

    void HandleAutoTestGoal()
    {
        if (!autoPublishTestGoal || debugGoalPublished || robotTransform == null)
            return;

        if (Time.time < autoPublishDelay)
            return;

        PublishRelativeTestGoal(testGoalDistance, "auto-test");
        debugGoalPublished = true;
    }

    void PublishRelativeTestGoal(float forwardDistance, string sourceLabel)
    {
        Vector3 desiredPoint = robotTransform.position + robotTransform.forward * forwardDistance;
        desiredPoint.y = 0f;
        Vector3 snappedPoint = SnapGoalToNearestOpenCell(desiredPoint);
        currentGoalPoint = snappedPoint;
        hasGoalPoint = true;
        PublishGoalPoint(snappedPoint, sourceLabel);
    }

    double ComputeGoalYawROS(Vector3 goalUnity)
    {
        if (robotTransform == null)
            return 0.0;

        Vector3 dir = goalUnity - robotTransform.position;
        if (dir.sqrMagnitude < 1e-4f)
            return 0.0;

        float yawUnity = Mathf.Atan2(dir.x, dir.z);
        return -yawUnity;
    }

    void CreateMarker()
    {
        goalMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        goalMarker.name = "GoalMarker";

        goalMarker.transform.localScale = new Vector3(markerRadius * 2f, 0.01f, markerRadius * 2f);
        Destroy(goalMarker.GetComponent<Collider>());

        Renderer rend = goalMarker.GetComponent<Renderer>();
        Material mat = new Material(rend.sharedMaterial);
        mat.color = new Color(1f, 0.08f, 0.08f, 1f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.8f, 0f, 0f));

        rend.material = mat;
        goalMarker.SetActive(false);
        markerReady = true;
    }

    void MoveMarker(Vector3 groundPoint)
    {
        if (!markerReady) return;

        goalMarker.transform.position = new Vector3(groundPoint.x, 0.02f, groundPoint.z);
        goalMarker.SetActive(true);
    }

    bool IsOutsideMapBounds(Vector3 point, out Vector2 min, out Vector2 max)
    {
        min = Vector2.zero;
        max = Vector2.zero;

        if (mapGenerator == null || mapGenerator.MapSize.x <= 0 || mapGenerator.MapSize.y <= 0)
            return false;

        float halfX = mapGenerator.MapSize.x * mapGenerator.tileSize * 0.5f;
        float halfZ = mapGenerator.MapSize.y * mapGenerator.tileSize * 0.5f;
        min = new Vector2(-halfX, -halfZ);
        max = new Vector2(halfX, halfZ);

        return point.x < min.x || point.x > max.x || point.z < min.y || point.z > max.y;
    }

    public void ClearGoalMarker()
    {
        if (goalMarker != null) goalMarker.SetActive(false);
    }

    static TimeMsg MakeStamp()
    {
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts = now - epoch;
        int sec = (int)ts.TotalSeconds;
        uint nsec = (uint)((ts.TotalSeconds - sec) * 1e9);
        return new TimeMsg { sec = sec, nanosec = nsec };
    }

    public string ActiveAlgorithm =>
        (availableAlgorithms != null && availableAlgorithms.Length > 0)
            ? availableAlgorithms[Mathf.Clamp(selectedAlgorithmIndex, 0, availableAlgorithms.Length - 1)]
            : "Unknown";

    public void SelectAlgorithm(int index)
    {
        if (availableAlgorithms == null || availableAlgorithms.Length == 0) return;
        selectedAlgorithmIndex = Mathf.Clamp(index, 0, availableAlgorithms.Length - 1);
        PublishPlannerSelection();
    }

    public void CycleAlgorithm()
    {
        if (availableAlgorithms == null || availableAlgorithms.Length == 0) return;
        selectedAlgorithmIndex = (selectedAlgorithmIndex + 1) % availableAlgorithms.Length;
        PublishPlannerSelection();
    }

    void PublishPlannerSelection()
    {
        if (ros == null || availableAlgorithms == null || availableAlgorithms.Length == 0) return;
        // 周期发布选择结果，允许 ROS 端节点后启动时也能拿到当前算法
        ros.Publish(PlannerSelectorTopic, new StringMsg(ActiveAlgorithm));
        Debug.Log($"GoalPublisher: 规划算法已切换 → {ActiveAlgorithm}");
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, Screen.height - 34, 260, 24),
            $"<b>规划算法: {ActiveAlgorithm}</b>  [Tab 切换]",
            new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true });
    }

    void OnDrawGizmosSelected()
    {
        if (goalMarker == null || !goalMarker.activeSelf) return;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.5f);
        Gizmos.DrawSphere(goalMarker.transform.position, markerRadius);
    }
}
