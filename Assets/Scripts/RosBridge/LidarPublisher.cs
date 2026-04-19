using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using System;

/// <summary>
/// 发布激光雷达数据。
/// </summary>
public class LidarPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/scan";
    public float publishFrequency = 10.0f;
    [Min(1)]
    public int publisherQueueSize = 50;

    [Header("Lidar Parameters")]
    [Range(0.01f, 10.0f)]
    public float maxRange = 3.5f;
    [Range(0.0f, 360.0f)]
    public float scanAngle = 360.0f;
    [Range(1, 1080)]
    public int samples = 360;
    public LayerMask obstacleLayer = ~0;

    [Header("Transform References")]
    public Transform lidarOrigin;

    private ROSConnection ros;
    private float timeElapsed;
    private LaserScanMsg scanMsg;
    private float angleIncrementRad;
    private float[] ranges;

    void Start()
    {
        if (lidarOrigin == null)
            lidarOrigin = transform;

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<LaserScanMsg>(topicName, publisherQueueSize);

        angleIncrementRad = (scanAngle * Mathf.Deg2Rad) / Mathf.Max(samples - 1, 1);
        ranges = new float[samples];

        scanMsg = new LaserScanMsg();
        scanMsg.header.frame_id = "base_scan";
        scanMsg.angle_min = -scanAngle * 0.5f * Mathf.Deg2Rad;
        scanMsg.angle_max = scanAngle * 0.5f * Mathf.Deg2Rad;
        scanMsg.angle_increment = angleIncrementRad;
        scanMsg.time_increment = 0.0f;
        scanMsg.scan_time = 1.0f / publishFrequency;
        scanMsg.range_min = 0.1f;
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
    /// 执行一次扫描。
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
                ranges[i] = float.NaN;
            }
        }
    }

    /// <summary>
    /// 发布扫描数据。
    /// </summary>
    void PublishScan()
    {
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        scanMsg.header.stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg { sec = sec, nanosec = nanosec };
        
        scanMsg.ranges = ranges;

        ros.Publish(topicName, scanMsg);
    }

    void OnDrawGizmosSelected()
    {
        if (lidarOrigin == null)
            return;

        Gizmos.color = Color.green;
        Vector3 origin = lidarOrigin.position;
        float startAngle = -scanAngle * 0.5f;
        int gizmoSamples = Mathf.Min(samples, 36);

        for (int i = 0; i < gizmoSamples; i++)
        {
            float angleDeg = startAngle + (i * scanAngle / Mathf.Max(gizmoSamples - 1, 1));
            Vector3 direction = lidarOrigin.rotation * Quaternion.Euler(0, angleDeg, 0) * Vector3.forward;
            Gizmos.DrawRay(origin, direction * maxRange);
        }
    }
}
