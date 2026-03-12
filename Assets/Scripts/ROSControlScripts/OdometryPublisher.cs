using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using System;

/// <summary>
/// 发布TurtleBot3的里程计信息（nav_msgs/Odometry）到ROS2的/odom话题
/// 支持两种计算模式：
/// 1. 差速里程计：通过左右轮的角速度推算机器人的运动（累积误差）
/// 2. 直接物理反馈：从机器人的ArticulationBody直接读取位姿和速度（更准确）
/// </summary>
public class OdometryPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/odom";
    public float publishFrequency = 30.0f; // Hz

    [Header("Odometry Source")]
    public OdometrySource source = OdometrySource.Differential;
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;
    public ArticulationBody robotBase; // 用于直接获取位姿和速度

    [Header("Robot Parameters")]
    public float wheelRadius = 0.033f; // 轮子半径 (米)
    public float wheelSeparation = 0.16f; // 轮距 (米)

    [Header("TF Settings")]
    public string odomFrameId = "odom";
    public string baseFrameId = "base_footprint";

    public enum OdometrySource
    {
        Differential,    // 通过轮子角速度计算
        DirectPhysics    // 直接使用机器人的物理状态
    }

    private ROSConnection ros;
    private float timeElapsed;
    private OdometryMsg odomMsg;
    private HeaderMsg header;
    private PoseWithCovarianceMsg pose;
    private TwistWithCovarianceMsg twist;

    // 差速里程计累积状态
    private Vector2 position = Vector2.zero; // x, y (平面)
    private float orientation = 0.0f; // 弧度
    private float lastLeftAngle = 0.0f;
    private float lastRightAngle = 0.0f;
    private bool firstUpdate = true;

    void Start()
    {
        // 初始化ROS连接
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<OdometryMsg>(topicName);

        // 初始化消息结构
        header = new HeaderMsg();
        header.frame_id = odomFrameId;

        pose = new PoseWithCovarianceMsg();
        pose.pose = new PoseMsg();
        pose.covariance = new double[36]; // 默认全零

        twist = new TwistWithCovarianceMsg();
        twist.twist = new TwistMsg();
        twist.covariance = new double[36];

        odomMsg = new OdometryMsg();
        odomMsg.header = header;
        odomMsg.child_frame_id = baseFrameId;
        odomMsg.pose = pose;
        odomMsg.twist = twist;

        // 根据模式进行验证
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
    /// 根据选择的源更新里程计数据
    /// </summary>
    void UpdateOdometry()
    {
        // 手动生成当前时间戳
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
    /// 差速里程计：通过轮子转角变化推算位姿和速度。
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

        // 更新位置和朝向
        orientation += deltaTheta;
        position.x += deltaDistance * Mathf.Cos(orientation);
        position.y += deltaDistance * Mathf.Sin(orientation);

        // 计算当前线速度和角速度（基于delta time）
        float dt = 1.0f / publishFrequency;
        float linearVel = deltaDistance / dt;
        float angularVel = deltaTheta / dt;

        // 填充pose
        pose.pose.position.x = position.x;
        pose.pose.position.y = position.y;
        pose.pose.position.z = 0.0f;
        pose.pose.orientation = Quaternion.Euler(0, 0, orientation * Mathf.Rad2Deg).ToRosQuaternion();

        // 填充twist
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
    /// 直接物理反馈：从机器人的ArticulationBody读取当前位姿和速度
    /// </summary>
    void UpdateDirectPhysicsOdometry()
    {
        if (robotBase == null)
            return;

        Vector3 worldPos = robotBase.transform.position;
        Quaternion worldRot = robotBase.transform.rotation;

        // 将位姿转换到odom坐标系（假设odom坐标系与世界坐标系对齐）
        pose.pose.position.x = worldPos.x;
        pose.pose.position.y = worldPos.y;
        pose.pose.position.z = worldPos.z;
        pose.pose.orientation = worldRot.ToRosQuaternion();

        // 速度（世界坐标系下的线速度和角速度）
        Vector3 linearVel = robotBase.velocity;
        Vector3 angularVel = robotBase.angularVelocity;

        twist.twist.linear.x = linearVel.x;
        twist.twist.linear.y = linearVel.y;
        twist.twist.linear.z = linearVel.z;
        twist.twist.angular.x = angularVel.x;
        twist.twist.angular.y = angularVel.y;
        twist.twist.angular.z = angularVel.z;
    }

    /// <summary>
    /// 获取轮子关节的当前角度（弧度）
    /// </summary>
    float GetWheelAngle(ArticulationBody wheel)
    {
        if (wheel == null)
            return 0.0f;

        return wheel.transform.localEulerAngles.x * Mathf.Deg2Rad;
    }

    /// <summary>
    /// 发布里程计消息到ROS
    /// </summary>
    void PublishOdometry()
    {
        ros.Publish(topicName, odomMsg);
    }

    /// <summary>
    /// 在Scene视图中绘制里程计坐标系（仅用于调试）
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (robotBase != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(robotBase.transform.position, 0.1f);
            Gizmos.DrawRay(robotBase.transform.position, robotBase.transform.forward * 0.3f);
        }
    }
}

/// <summary>
/// 扩展方法：将Unity的Quaternion转换为ROS的geometry_msgs/Quaternion
/// </summary>
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