using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using System;

/// <summary>
/// 模拟TurtleBot3的激光雷达，通过射线检测生成LaserScan数据并发布到ROS2的/scan话题
/// </summary>
public class LidarPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/scan";
    public float publishFrequency = 10.0f; // Hz

    [Header("Lidar Parameters")]
    [Range(0.01f, 10.0f)]
    public float maxRange = 3.5f; // 最大探测距离 (米)
    [Range(0.0f, 360.0f)]
    public float scanAngle = 360.0f; // 扫描角度 (度)
    [Range(1, 1080)]
    public int samples = 360; // 射线数量 (分辨率)
    public LayerMask obstacleLayer = ~0; // 检测的层

    [Header("Transform References")]
    public Transform lidarOrigin; // 激光雷达的发射原点（如base_scan链接）

    private ROSConnection ros;
    private float timeElapsed;
    private LaserScanMsg scanMsg;
    private float angleIncrementRad;
    private float[] ranges;

    void Start()
    {
        // 如果没有指定原点，使用当前GameObject的Transform
        if (lidarOrigin == null)
            lidarOrigin = transform;

        // 初始化ROS连接
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<LaserScanMsg>(topicName);

        // 计算角度增量（弧度）
        angleIncrementRad = (scanAngle * Mathf.Deg2Rad) / Mathf.Max(samples - 1, 1);

        // 预分配距离数组
        ranges = new float[samples];

        // 初始化LaserScan消息的固定字段
        scanMsg = new LaserScanMsg();
        scanMsg.header.frame_id = "base_scan"; // 坐标系名称，应与TF树中的名称一致
        scanMsg.angle_min = -scanAngle * 0.5f * Mathf.Deg2Rad; // 起始角度（弧度）
        scanMsg.angle_max = scanAngle * 0.5f * Mathf.Deg2Rad;  // 结束角度（弧度）
        scanMsg.angle_increment = angleIncrementRad;
        scanMsg.time_increment = 0.0f; // 假设所有射线同时发射
        scanMsg.scan_time = 1.0f / publishFrequency; // 扫描一周期的时间
        scanMsg.range_min = 0.1f; // 最小有效距离
        scanMsg.range_max = maxRange;

        Debug.Log($"LidarPublisher initialized: {samples} rays, {scanAngle}°, max range {maxRange}m, publishing at {publishFrequency}Hz");
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;
        float interval = 1.0f / publishFrequency;

        if (timeElapsed >= interval)
        {
            PerformScan();
            PublishScan();
            timeElapsed = 0;
        }
    }

    /// <summary>
    /// 执行一次完整的扫描，填充距离数组
    /// </summary>
    void PerformScan()
    {
        Vector3 origin = lidarOrigin.position;
        float startAngle = -scanAngle * 0.5f; // 度

        for (int i = 0; i < samples; i++)
        {
            float angleDeg = startAngle + (i * scanAngle / Mathf.Max(samples - 1, 1));
            Vector3 direction = lidarOrigin.rotation * Quaternion.Euler(0, angleDeg, 0) * Vector3.forward;

            RaycastHit hit;
            if (Physics.Raycast(origin, direction, out hit, maxRange, obstacleLayer))
            {
                ranges[i] = hit.distance;
            }
            else
            {
                ranges[i] = float.NaN; // ROS中NaN表示超出范围
            }

            // 可选：可视化调试射线
            // Debug.DrawRay(origin, direction * maxRange, Color.red, 0.05f);
        }
    }

    /// <summary>
    /// 发布扫描数据到ROS
    /// </summary>
    void PublishScan()
    {
        // 手动生成当前时间戳
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        scanMsg.header.stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg { sec = sec, nanosec = nanosec };
        
        scanMsg.ranges = ranges;

        ros.Publish(topicName, scanMsg);
    }

    /// <summary>
    /// 在Scene视图中绘制激光雷达的探测范围（仅用于调试）
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (lidarOrigin == null)
            return;

        Gizmos.color = Color.green;
        Vector3 origin = lidarOrigin.position;
        float startAngle = -scanAngle * 0.5f;
        int gizmoSamples = Mathf.Min(samples, 36); // 减少Gizmo绘制数量

        for (int i = 0; i < gizmoSamples; i++)
        {
            float angleDeg = startAngle + (i * scanAngle / Mathf.Max(gizmoSamples - 1, 1));
            Vector3 direction = lidarOrigin.rotation * Quaternion.Euler(0, angleDeg, 0) * Vector3.forward;
            Gizmos.DrawRay(origin, direction * maxRange);
        }
    }
}