using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System;

/// <summary>
/// 鼠标左键点击地图地面，将点击位置作为导航目标点发布到 ROS2 /goal_pose 话题。
///
/// 坐标系转换（与 OdometryPublisher 一致）：
///   Unity(X右, Y上, Z前) → ROS(X前, Y左, Z上)
///   ros.x = unity.z，ros.y = -unity.x
///
/// 目标朝向：机器人当前位置指向目标点的方向（便于 Nav2 以目标姿态抵达）。
/// 若未指定机器人引用，朝向默认为 ROS 坐标系正前方（yaw = 0）。
///
/// 用法：
///   将此组件挂载到场景中任意常驻 GameObject（如 ROS 管理对象）。
///   在 Inspector 中拖入 robotTransform（机器人根节点）。
/// </summary>
public class GoalPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/goal_pose";
    public string frameId = "map";

    [Header("Robot Reference")]
    [Tooltip("机器人根 Transform，用于计算目标朝向。留空则朝向默认为正前方（yaw=0）。")]
    public Transform robotTransform;

    [Header("Visual Marker")]
    [Tooltip("红色目标点指示器的半径（米）")]
    [Range(0.05f, 0.5f)]
    public float markerRadius = 0.12f;

    private ROSConnection ros;
    private GameObject goalMarker;
    private bool markerReady = false;

    // ── Unity 生命周期 ───────────────────────────────────────────────────────

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(topicName);

        CreateMarker();
        Debug.Log($"GoalPublisher: 已启动，点击地面发布目标到 {topicName}");
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        // 防止点击穿透 UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        TryPublishGoal();
    }

    void OnDestroy()
    {
        if (goalMarker != null)
            Destroy(goalMarker);
    }

    // ── 核心逻辑 ─────────────────────────────────────────────────────────────

    void TryPublishGoal()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("GoalPublisher: 未找到 Main Camera。");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // 与 y=0 平面求交（地图地面均位于 Unity y=0）
        if (Mathf.Abs(ray.direction.y) < 1e-6f) return; // 射线平行于地面

        float t = -ray.origin.y / ray.direction.y;
        if (t < 0f) return; // 交点在相机背后

        Vector3 hitPoint = new Vector3(
            ray.origin.x + t * ray.direction.x,
            0f,
            ray.origin.z + t * ray.direction.z
        );

        // 更新红点位置
        MoveMarker(hitPoint);

        // Unity → ROS 坐标转换
        double rosX = hitPoint.z;
        double rosY = -hitPoint.x;

        // 计算目标朝向（ROS yaw）
        double yawROS = ComputeGoalYawROS(hitPoint);

        // 构建并发布消息
        var stamp = MakeStamp();
        var msg = new PoseStampedMsg
        {
            header = new HeaderMsg
            {
                stamp    = stamp,
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
        Debug.Log($"GoalPublisher: 目标已发布 " +
                  $"Unity({hitPoint.x:F2}, {hitPoint.z:F2}) → " +
                  $"ROS({rosX:F2}, {rosY:F2})，yaw={yawROS * Mathf.Rad2Deg:F1}°");
    }

    /// <summary>
    /// 计算目标朝向：从机器人位置指向目标点，转换为 ROS yaw（绕 Z 轴）。
    /// </summary>
    double ComputeGoalYawROS(Vector3 goalUnity)
    {
        if (robotTransform == null) return 0.0;

        Vector3 dir = goalUnity - robotTransform.position;
        if (dir.sqrMagnitude < 1e-4f) return 0.0; // 目标与机器人重合

        // Unity 偏航角：绕 Y 轴，atan2(X, Z)
        float yawUnity = Mathf.Atan2(dir.x, dir.z);

        // ROS 偏航角：Unity Y 顺时针 = ROS Z 逆时针，取反
        return -yawUnity;
    }

    // ── 红点指示器 ──────────────────────────────────────────────────────────

    void CreateMarker()
    {
        goalMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        goalMarker.name = "GoalMarker";

        // 压扁成薄圆饼，高度 = 0.02m，半径由 markerRadius 控制
        goalMarker.transform.localScale = new Vector3(markerRadius * 2f, 0.01f, markerRadius * 2f);

        // 移除碰撞体，避免干扰后续射线检测
        Destroy(goalMarker.GetComponent<Collider>());

        // 设置红色自发光材质（不受光照影响，在任意光照下都醒目）
        Renderer rend = goalMarker.GetComponent<Renderer>();
        Material mat = new Material(rend.sharedMaterial);
        mat.color = new Color(1f, 0.08f, 0.08f, 1f);

        // URP 自发光（若 shader 支持）
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.8f, 0f, 0f));

        rend.material = mat;
        goalMarker.SetActive(false);
        markerReady = true;
    }

    void MoveMarker(Vector3 groundPoint)
    {
        if (!markerReady) return;

        // 略微抬高以避免 Z-fighting
        goalMarker.transform.position = new Vector3(groundPoint.x, 0.02f, groundPoint.z);
        goalMarker.SetActive(true);
    }

    // ── 工具方法 ─────────────────────────────────────────────────────────────

    static TimeMsg MakeStamp()
    {
        var now   = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts    = now - epoch;
        int sec   = (int)ts.TotalSeconds;
        uint nsec = (uint)((ts.TotalSeconds - sec) * 1e9);
        return new TimeMsg { sec = sec, nanosec = nsec };
    }

    void OnDrawGizmosSelected()
    {
        if (goalMarker == null || !goalMarker.activeSelf) return;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.5f);
        Gizmos.DrawSphere(goalMarker.transform.position, markerRadius);
    }
}
