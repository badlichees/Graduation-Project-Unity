using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

public class TurtleBotController : MonoBehaviour
{
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;

    [Header("Robot Specs")]
    public float wheelRadius = 0.033f;
    public float wheelSeparation = 0.16f;

    [Header("Physics Tuning")]
    public float driveDamping = 1000f; 
    public float driveForceLimit = 2000f;

    [Header("Direction Fix")]
    public bool invertLeftWheel = false;
    public bool invertRightWheel = false;

    private float targetLinearVel;
    private float targetAngularVel;

    void Start()
    {
        SetupWheel(wheelLeftJoint);
        SetupWheel(wheelRightJoint);
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>("/cmd_vel", OnCmdVelReceived);
        Debug.Log("ROS2 TurtleBot Controller Started.");
    }

    void SetupWheel(ArticulationBody body)
    {
        if (body == null) return;
        body.linearDamping = 0f;      // 显式消除线性阻尼
        body.angularDamping = 0f;     // 显式消除旋转阻尼
        body.jointFriction = 0f;      // 显式消除关节内部摩擦
        body.sleepThreshold = 0f;     // 严禁进入睡眠
        
        var drive = body.xDrive;
        drive.stiffness = 0;
        drive.damping = driveDamping; 
        drive.forceLimit = driveForceLimit;
        body.xDrive = drive;
    }

    void OnCmdVelReceived(TwistMsg msg)
    {
        // 如果通信通了，这行代码会疯狂在控制台刷屏，显示收到的速度
        Debug.LogWarning($"收到ROS指令! Linear: {msg.linear.x}, Angular: {msg.angular.z}");
        
        targetLinearVel = (float)msg.linear.x;
        targetAngularVel = (float)msg.angular.z;
    }

    void FixedUpdate()
    {
        if (wheelLeftJoint == null || wheelRightJoint == null) return;

        // 差速解算
        float leftRadPerSec = (targetLinearVel - targetAngularVel * wheelSeparation / 2f) / wheelRadius;
        float rightRadPerSec = (targetLinearVel + targetAngularVel * wheelSeparation / 2f) / wheelRadius;

        // 应用反转开关
        float finalLeft = invertLeftWheel ? -leftRadPerSec : leftRadPerSec;
        float finalRight = invertRightWheel ? -rightRadPerSec : rightRadPerSec;

        ApplyVelocity(wheelLeftJoint, finalLeft * Mathf.Rad2Deg);
        ApplyVelocity(wheelRightJoint, finalRight * Mathf.Rad2Deg);

        // 每隔1秒在控制台输出一下当前的驱动数值，如果全是0说明没收到ROS消息
        if (Time.frameCount % 50 == 0 && (targetLinearVel != 0 || targetAngularVel != 0))
        {
            Debug.Log($"Driving Wheels - Left: {finalLeft * Mathf.Rad2Deg:F0} deg/s, Right: {finalRight * Mathf.Rad2Deg:F0} deg/s");
        }
    }

    void ApplyVelocity(ArticulationBody body, float targetDegPerSec)
    {
        var drive = body.xDrive;
        drive.targetVelocity = targetDegPerSec;
        body.xDrive = drive;
        // 强制唤醒物理引擎
        if (body.IsSleeping()) body.WakeUp();
    }
}