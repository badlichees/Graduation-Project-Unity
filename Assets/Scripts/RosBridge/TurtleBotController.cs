using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

/// <summary>
/// 订阅 Nav2（或 teleop）发布的 /cmd_vel（geometry_msgs/Twist），
/// 在 Unity 侧完成差速运动学解算，直接驱动左右轮 ArticulationBody。
///
/// 差速解算（标准公式）：
///   left_ω  = (v - ω_z * d/2) / r
///   right_ω = (v + ω_z * d/2) / r
///   v   = cmd_vel.linear.x（m/s）
///   ω_z = cmd_vel.angular.z（rad/s，ROS 正方向 = 逆时针 = 左转）
///   d   = wheelSeparation（轮距，m）
///   r   = wheelRadius（轮半径，m）
///
/// 若机器人旋转方向与预期相反，先尝试勾选 invertAngular；
/// 若前进/后退方向反，勾选 invertLinear。
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
    [Tooltip("若机器人旋转方向反了（转圈），勾选此项")]
    public bool invertAngular = false;
    [Tooltip("若机器人前进/后退方向反了，勾选此项")]
    public bool invertLinear  = false;

    [Header("ROS Settings")]
    public string cmdVelTopic = "/cmd_vel";

    private ROSConnection ros;

    // ── Unity 生命周期 ────────────────────────────────────────────────────────

    void Start()
    {
        SetupWheel(wheelLeftJoint);
        SetupWheel(wheelRightJoint);

        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<TwistMsg>(cmdVelTopic, OnCmdVelReceived);

        Debug.Log($"TurtleBotController: 已订阅 {cmdVelTopic}，直接处理 cmd_vel");
    }

    // ── 回调：差速解算并驱动轮子 ─────────────────────────────────────────────

    void OnCmdVelReceived(TwistMsg msg)
    {
        float linear  = (float)msg.linear.x  * (invertLinear  ? -1f : 1f);
        float angular = (float)msg.angular.z * (invertAngular ? -1f : 1f);

        // 标准差速运动学
        float leftRadPS  = (linear - angular * wheelSeparation * 0.5f) / wheelRadius;
        float rightRadPS = (linear + angular * wheelSeparation * 0.5f) / wheelRadius;

        ApplyVelocity(wheelLeftJoint,  leftRadPS  * Mathf.Rad2Deg);
        ApplyVelocity(wheelRightJoint, rightRadPS * Mathf.Rad2Deg);
    }

    // ── 工具方法 ─────────────────────────────────────────────────────────────

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
}
