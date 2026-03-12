using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System;

/// <summary>
/// 发布TurtleBot3的关节状态（/joint_states）到ROS2
/// 收集左右轮的转角（弧度）和角速度（弧度/秒），并按照URDF中的关节名称发布
/// 与ROS2的robot_state_publisher节点配合，由后者生成TF树
/// </summary>
public class JointStatePublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/joint_states";
    public float publishFrequency = 30.0f; // Hz

    [Header("Wheel Joints")]
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;

    [Header("Joint Names")]
    public string leftWheelJointName = "wheel_left_joint";
    public string rightWheelJointName = "wheel_right_joint";

    private ROSConnection ros;
    private float timeElapsed;
    private JointStateMsg jointStateMsg;
    private HeaderMsg header;

    // 用于计算角速度的上一次角度
    private float lastLeftAngle = 0.0f;
    private float lastRightAngle = 0.0f;
    private bool firstUpdate = true;

    void Start()
    {
        // 初始化ROS连接
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(topicName);

        // 初始化消息结构
        header = new HeaderMsg();
        header.frame_id = ""; // joint_states的header通常为空

        jointStateMsg = new JointStateMsg();
        jointStateMsg.header = header;
        // 关节名称数组（顺序与位置、速度数组对应）
        jointStateMsg.name = new string[] { leftWheelJointName, rightWheelJointName };
        // 预分配数组
        jointStateMsg.position = new double[2];
        jointStateMsg.velocity = new double[2];
        jointStateMsg.effort = new double[0]; // 不发布effort

        // 获取初始角度
        if (wheelLeftJoint != null)
            lastLeftAngle = GetWheelAngle(wheelLeftJoint);
        if (wheelRightJoint != null)
            lastRightAngle = GetWheelAngle(wheelRightJoint);

        Debug.Log($"JointStatePublisher initialized: publishing {leftWheelJointName}, {rightWheelJointName} at {publishFrequency}Hz");
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;
        float interval = 1.0f / publishFrequency;

        if (timeElapsed >= interval)
        {
            UpdateJointStates();
            PublishJointStates();
            timeElapsed = 0;
        }
    }

    /// <summary>
    /// 更新关节状态数据
    /// </summary>
    void UpdateJointStates()
    {
        // 更新时间戳
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        header.stamp = new TimeMsg { sec = sec, nanosec = nanosec };

        float leftAngle = GetWheelAngle(wheelLeftJoint);
        float rightAngle = GetWheelAngle(wheelRightJoint);

        // 计算角速度（弧度/秒）
        float dt = 1.0f / publishFrequency;
        float leftVel = 0.0f;
        float rightVel = 0.0f;

        if (!firstUpdate)
        {
            // 使用角度差除以时间，注意处理角度环绕
            leftVel = Mathf.DeltaAngle(lastLeftAngle * Mathf.Rad2Deg, leftAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt;
            rightVel = Mathf.DeltaAngle(lastRightAngle * Mathf.Rad2Deg, rightAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt;
        }
        else
        {
            firstUpdate = false;
        }

        jointStateMsg.position[0] = leftAngle;
        jointStateMsg.position[1] = rightAngle;
        jointStateMsg.velocity[0] = leftVel;
        jointStateMsg.velocity[1] = rightVel;

        lastLeftAngle = leftAngle;
        lastRightAngle = rightAngle;
    }

    /// <summary>
    /// 获取轮子关节的当前角度（弧度）
    /// ArticulationBody的jointPosition属性可能直接返回弧度，但需要根据关节类型确认
    /// </summary>
    float GetWheelAngle(ArticulationBody wheel)
    {
        if (wheel == null)
            return 0.0f;

        return wheel.transform.localEulerAngles.x * Mathf.Deg2Rad;
    }

    /// <summary>
    /// 发布关节状态消息到ROS
    /// </summary>
    void PublishJointStates()
    {
        ros.Publish(topicName, jointStateMsg);
    }

    /// <summary>
    /// 在Inspector中重置关节名称到默认值
    /// </summary>
    void Reset()
    {
        leftWheelJointName = "wheel_left_joint";
        rightWheelJointName = "wheel_right_joint";
    }
}