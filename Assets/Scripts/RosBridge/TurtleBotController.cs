using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

// Nav2 只发布底盘速度，Unity 侧仍需要把 /cmd_vel 适配成左右轮关节速度
public class TurtleBotController : MonoBehaviour
{
    [Header("Wheel References")]
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;

    [Header("Robot Specs")]
    public float wheelRadius = 0.033f;
    public float wheelSeparation = 0.160f;

    [Header("Physics Tuning")]
    public float driveDamping = 1000f;
    public float driveForceLimit = 2000f;

    [Header("Direction Fix")]
    [Tooltip("旋转方向反时启用")]
    public bool invertAngular = false;
    [Tooltip("前进方向反时启用")]
    public bool invertLinear  = false;
    [Tooltip("左轮方向反时启用")]
    public bool invertLeftWheel = false;
    [Tooltip("右轮方向反时启用")]
    public bool invertRightWheel = false;

    [Header("ROS Settings")]
    public string cmdVelTopic = "/cmd_vel";

    [Header("Debug")]
    [Tooltip("输出控制调试日志")]
    public bool enableDebugLogs = false;
    [Min(0.1f)]
    public float debugLogInterval = 0.5f;
    public bool warnOnCmdVelTimeout = true;
    [Min(0.1f)]
    public float cmdVelTimeoutSeconds = 1.5f;
    public bool warnOnZeroCmdVel = true;
    [Min(0.1f)]
    public float zeroCmdVelWarningSeconds = 1.0f;

    private ROSConnection ros;
    private float nextDebugLogTime;
    private float lastCmdVelTime = -1f;
    private float zeroCmdVelSince = -1f;
    private bool warnedCmdVelTimeout;
    private bool warnedZeroCmdVel;

    void Start()
    {
        SetupWheel(wheelLeftJoint);
        SetupWheel(wheelRightJoint);

        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<TwistMsg>(cmdVelTopic, OnCmdVelReceived);

        Debug.Log($"TurtleBotController: 已订阅 {cmdVelTopic}，直接处理 cmd_vel");
    }

    void Update()
    {
        MaybeWarnCommandTimeout();
    }

    void OnCmdVelReceived(TwistMsg msg)
    {
        lastCmdVelTime = Time.time;
        warnedCmdVelTimeout = false;

        float linear  = (float)msg.linear.x  * (invertLinear  ? -1f : 1f);
        float angular = (float)msg.angular.z * (invertAngular ? -1f : 1f);
        TrackZeroCommand(linear, angular);

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

    void MaybeWarnCommandTimeout()
    {
        if (!warnOnCmdVelTimeout || warnedCmdVelTimeout || !PlannerStatsStore.HasActiveRun)
            return;

        float elapsed = lastCmdVelTime < 0f
            ? PlannerStatsStore.ActiveRunSeconds
            : Time.time - lastCmdVelTime;

        if (elapsed < cmdVelTimeoutSeconds)
            return;

        warnedCmdVelTimeout = true;
        Debug.LogWarning(
            $"TurtleBotController: active goal but no {cmdVelTopic} for {elapsed:F1}s. " +
            "Nav2 may have stopped or disconnected.");
    }

    void TrackZeroCommand(float linear, float angular)
    {
        if (!warnOnZeroCmdVel || !PlannerStatsStore.HasActiveRun)
        {
            zeroCmdVelSince = -1f;
            warnedZeroCmdVel = false;
            return;
        }

        bool isZero = Mathf.Abs(linear) < 1e-4f && Mathf.Abs(angular) < 1e-4f;
        if (!isZero)
        {
            zeroCmdVelSince = -1f;
            warnedZeroCmdVel = false;
            return;
        }

        if (zeroCmdVelSince < 0f)
            zeroCmdVelSince = Time.time;

        if (warnedZeroCmdVel || Time.time - zeroCmdVelSince < zeroCmdVelWarningSeconds)
            return;

        warnedZeroCmdVel = true;
        Debug.LogWarning(
            $"TurtleBotController: receiving zero {cmdVelTopic} for " +
            $"{Time.time - zeroCmdVelSince:F1}s while an active goal is running.");
    }
}
