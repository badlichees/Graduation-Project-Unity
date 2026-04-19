using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

/// <summary>
/// 接收 /cmd_vel 并驱动机器人底盘。
/// </summary>
public class TurtleBotController : MonoBehaviour
{
    [Header("Wheel References")]
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;

    [Header("Robot Specs")]
    public float wheelRadius    = 0.033f;  // 单位：m
    public float wheelSeparation = 0.160f; // 单位：m

    [Header("Physics Tuning")]
    public float driveDamping   = 1000f;
    public float driveForceLimit = 2000f;

    [Header("Direction Fix")]
    [Tooltip("旋转方向反时启用。")]
    public bool invertAngular = false;
    [Tooltip("前进方向反时启用。")]
    public bool invertLinear  = false;
    [Tooltip("左轮方向反时启用。")]
    public bool invertLeftWheel = false;
    [Tooltip("右轮方向反时启用。")]
    public bool invertRightWheel = false;

    [Header("ROS Settings")]
    public string cmdVelTopic = "/cmd_vel";

    [Header("Debug")]
    [Tooltip("输出控制调试日志。")]
    public bool enableDebugLogs = false;
    [Min(0.1f)]
    public float debugLogInterval = 0.5f;

    private ROSConnection ros;
    private float nextDebugLogTime;

    void Start()
    {
        SetupWheel(wheelLeftJoint);
        SetupWheel(wheelRightJoint);

        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<TwistMsg>(cmdVelTopic, OnCmdVelReceived);

        Debug.Log($"TurtleBotController: 已订阅 {cmdVelTopic}，直接处理 cmd_vel");
    }

    void OnCmdVelReceived(TwistMsg msg)
    {
        float linear  = (float)msg.linear.x  * (invertLinear  ? -1f : 1f);
        float angular = (float)msg.angular.z * (invertAngular ? -1f : 1f);

        float leftRadPS  = (linear - angular * wheelSeparation * 0.5f) / wheelRadius;
        float rightRadPS = (linear + angular * wheelSeparation * 0.5f) / wheelRadius;

        if (invertLeftWheel) leftRadPS = -leftRadPS;
        if (invertRightWheel) rightRadPS = -rightRadPS;

        ApplyVelocity(wheelLeftJoint,  leftRadPS  * Mathf.Rad2Deg);
        ApplyVelocity(wheelRightJoint, rightRadPS * Mathf.Rad2Deg);

        MaybeLogCommand(linear, angular, leftRadPS, rightRadPS);
    }

    void SetupWheel(ArticulationBody body)
    {
        if (body == null) return;
        body.linearDamping  = 0f;
        body.angularDamping = 0f;
        body.jointFriction  = 0f;
        body.sleepThreshold = 0f;

        var drive = body.xDrive;
        drive.stiffness   = 0f;
        drive.damping     = driveDamping;
        drive.forceLimit  = driveForceLimit;
        body.xDrive = drive;
    }

    void ApplyVelocity(ArticulationBody body, float targetDegPerSec)
    {
        if (body == null) return;
        var drive = body.xDrive;
        drive.targetVelocity = targetDegPerSec;
        body.xDrive = drive;
        if (body.IsSleeping()) body.WakeUp();
    }

    void MaybeLogCommand(float linear, float angular, float leftRadPS, float rightRadPS)
    {
        if (!enableDebugLogs || Time.time < nextDebugLogTime) return;

        nextDebugLogTime = Time.time + debugLogInterval;
        Debug.Log(
            $"TurtleBotController cmd_vel: linear={linear:F3} m/s, angular={angular:F3} rad/s, " +
            $"left={leftRadPS:F3} rad/s, right={rightRadPS:F3} rad/s, " +
            $"invertLinear={invertLinear}, invertAngular={invertAngular}, " +
            $"invertLeftWheel={invertLeftWheel}, invertRightWheel={invertRightWheel}"
        );
    }
}