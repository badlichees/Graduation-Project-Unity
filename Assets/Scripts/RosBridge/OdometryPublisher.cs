using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using System;

/// <summary>
/// 发布机器人里程计。
/// </summary>
public class OdometryPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/odom";
    public float publishFrequency = 30.0f;
    [Min(1)]
    public int publisherQueueSize = 100;

    [Header("Odometry Source")]
    public OdometrySource source = OdometrySource.Differential;
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;
    public ArticulationBody robotBase;

    [Header("Robot Parameters")]
    public float wheelRadius = 0.033f;
    public float wheelSeparation = 0.16f;

    [Header("TF Settings")]
    public string odomFrameId = "odom";
    public string baseFrameId = "base_footprint";

    [Header("Debug")]
    [Tooltip("输出里程计调试日志。")]
    public bool enableDebugLogs = false;
    [Min(0.1f)]
    public float debugLogInterval = 0.5f;

    public enum OdometrySource
    {
        Differential,
        DirectPhysics
    }

    private ROSConnection ros;
    private float timeElapsed;
    private OdometryMsg odomMsg;
    private HeaderMsg header;
    private PoseWithCovarianceMsg pose;
    private TwistWithCovarianceMsg twist;

    private Vector2 position = Vector2.zero;
    private float orientation = 0.0f;
    private float lastLeftAngle = 0.0f;
    private float lastRightAngle = 0.0f;
    private bool firstUpdate = true;
    private float nextDebugLogTime;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<OdometryMsg>(topicName, publisherQueueSize);

        header = new HeaderMsg();
        header.frame_id = odomFrameId;

        pose = new PoseWithCovarianceMsg();
        pose.pose = new PoseMsg();
        pose.covariance = new double[36];

        twist = new TwistWithCovarianceMsg();
        twist.twist = new TwistMsg();
        twist.covariance = new double[36];

        odomMsg = new OdometryMsg();
        odomMsg.header = header;
        odomMsg.child_frame_id = baseFrameId;
        odomMsg.pose = pose;
        odomMsg.twist = twist;

        if (source == OdometrySource.Differential)
        {
            if (wheelLeftJoint == null || wheelRightJoint == null)
            {
                Debug.LogError("差速里程计模式需要设置左右轮关节！");
            }
            else
            {
                lastLeftAngle = GetWheelAngle(wheelLeftJoint);
                lastRightAngle = GetWheelAngle(wheelRightJoint);
            }
        }
        else
        {
            if (robotBase == null)
                robotBase = GetComponent<ArticulationBody>();
            if (robotBase == null)
                Debug.LogError("直接物理模式需要设置robotBase或确保GameObject上有ArticulationBody组件！");
        }

        Debug.Log($"OdometryPublisher initialized: source={source}, publishing at {publishFrequency}Hz");
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;
        float interval = 1.0f / publishFrequency;

        if (timeElapsed >= interval)
        {
            UpdateOdometry();
            PublishOdometry();
            timeElapsed = 0;
        }
    }

    /// <summary>
    /// 更新里程计数据。
    /// </summary>
    void UpdateOdometry()
    {
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        header.stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg { sec = sec, nanosec = nanosec };

        if (source == OdometrySource.Differential)
        {
            UpdateDifferentialOdometry();
        }
        else
        {
            UpdateDirectPhysicsOdometry();
        }
    }

    /// <summary>
    /// 使用轮速积分里程计。
    /// </summary>
    void UpdateDifferentialOdometry()
    {
        float leftAngle = GetWheelAngle(wheelLeftJoint);
        float rightAngle = GetWheelAngle(wheelRightJoint);

        if (firstUpdate)
        {
            lastLeftAngle = leftAngle;
            lastRightAngle = rightAngle;
            firstUpdate = false;
            return;
        }

        float deltaLeft = Mathf.DeltaAngle(lastLeftAngle * Mathf.Rad2Deg, leftAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float deltaRight = Mathf.DeltaAngle(lastRightAngle * Mathf.Rad2Deg, rightAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        float deltaDistance = (deltaLeft + deltaRight) * wheelRadius * 0.5f;
        float deltaTheta = (deltaRight - deltaLeft) * wheelRadius / wheelSeparation;

        orientation += deltaTheta;
        position.x += deltaDistance * Mathf.Cos(orientation);
        position.y += deltaDistance * Mathf.Sin(orientation);

        float dt = 1.0f / publishFrequency;
        float linearVel = deltaDistance / dt;
        float angularVel = deltaTheta / dt;

        pose.pose.position.x = position.x;
        pose.pose.position.y = position.y;
        pose.pose.position.z = 0.0f;
        pose.pose.orientation = Quaternion.Euler(0, 0, orientation * Mathf.Rad2Deg).ToRosQuaternion();

        twist.twist.linear.x = linearVel;
        twist.twist.linear.y = 0.0f;
        twist.twist.linear.z = 0.0f;
        twist.twist.angular.x = 0.0f;
        twist.twist.angular.y = 0.0f;
        twist.twist.angular.z = angularVel;

        lastLeftAngle = leftAngle;
        lastRightAngle = rightAngle;
    }

    /// <summary>
    /// 直接读取物理状态。
    /// </summary>
    void UpdateDirectPhysicsOdometry()
    {
        if (robotBase == null)
            return;

        Vector3 worldPos = robotBase.transform.position;
        Quaternion worldRot = robotBase.transform.rotation;

        pose.pose.position.x = worldPos.z;
        pose.pose.position.y = -worldPos.x;
        pose.pose.position.z = 0.0;

        float yawUnity = Mathf.Atan2(
            2f * (worldRot.x * worldRot.z + worldRot.w * worldRot.y),
            1f - 2f * (worldRot.x * worldRot.x + worldRot.y * worldRot.y)
        );
        float yawROS = -yawUnity;
        pose.pose.orientation = new RosMessageTypes.Geometry.QuaternionMsg
        {
            x = 0.0,
            y = 0.0,
            z = Math.Sin(yawROS * 0.5),
            w = Math.Cos(yawROS * 0.5)
        };

        Vector3 linearVel = robotBase.velocity;
        twist.twist.linear.x = linearVel.z;
        twist.twist.linear.y = -linearVel.x;
        twist.twist.linear.z = 0.0;

        Vector3 angularVel = robotBase.angularVelocity;
        twist.twist.angular.x = 0.0;
        twist.twist.angular.y = 0.0;
        twist.twist.angular.z = -angularVel.y;

        MaybeLogDirectPhysics(worldPos, yawUnity, yawROS, linearVel, angularVel);
    }

    /// <summary>
    /// 读取轮子角度。
    /// </summary>
    float GetWheelAngle(ArticulationBody wheel)
    {
        if (wheel == null)
            return 0.0f;

        return wheel.transform.localEulerAngles.x * Mathf.Deg2Rad;
    }

    /// <summary>
    /// 发布里程计消息。
    /// </summary>
    void PublishOdometry()
    {
        ros.Publish(topicName, odomMsg);
    }

    void OnDrawGizmosSelected()
    {
        if (robotBase != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(robotBase.transform.position, 0.1f);
            Gizmos.DrawRay(robotBase.transform.position, robotBase.transform.forward * 0.3f);
        }
    }

    void MaybeLogDirectPhysics(
        Vector3 worldPos,
        float yawUnity,
        float yawROS,
        Vector3 linearVel,
        Vector3 angularVel)
    {
        if (!enableDebugLogs || source != OdometrySource.DirectPhysics || Time.time < nextDebugLogTime)
            return;

        nextDebugLogTime = Time.time + debugLogInterval;
        Debug.Log(
            $"OdometryPublisher DirectPhysics: " +
            $"unityPos=({worldPos.x:F3}, {worldPos.z:F3}), " +
            $"yawUnity={yawUnity * Mathf.Rad2Deg:F1} deg, yawROS={yawROS * Mathf.Rad2Deg:F1} deg, " +
            $"unityVel=({linearVel.x:F3}, {linearVel.z:F3}) m/s, " +
            $"unityAngularY={angularVel.y:F3} rad/s, odomAngularZ={twist.twist.angular.z:F3} rad/s"
        );
    }
}

/// <summary>Quaternion 转 ROS 消息。</summary>
public static class QuaternionExtensions
{
    public static RosMessageTypes.Geometry.QuaternionMsg ToRosQuaternion(this Quaternion q)
    {
        return new RosMessageTypes.Geometry.QuaternionMsg
        {
            x = q.x,
            y = q.y,
            z = q.z,
            w = q.w
        };
    }
}