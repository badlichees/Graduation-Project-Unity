using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;   // Float64Msg需要先生成

public class TurtleBotController : MonoBehaviour
{
    public ArticulationBody wheelLeftJoint;
    public ArticulationBody wheelRightJoint;

    [Header("Robot Specs")]
    public float wheelRadius = 0.033f; // 仅用于Debug显示，不参与计算
    public float wheelSeparation = 0.16f;

    [Header("Physics Tuning")]
    public float driveDamping = 1000f; 
    public float driveForceLimit = 2000f;

    [Header("Direction Fix")]
    public bool invertLeftWheel = false;
    public bool invertRightWheel = false;

    private ROSConnection ros;

    void Start()
    {
        SetupWheel(wheelLeftJoint);
        SetupWheel(wheelRightJoint);
        
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<Float64Msg>("/left_wheel_vel", OnLeftWheelVelReceived);
        ros.Subscribe<Float64Msg>("/right_wheel_vel", OnRightWheelVelReceived);
        
        Debug.Log("ROS2 差速控制器已连接，Unity仅负责执行");
    }

    // 物理设置逻辑
    void SetupWheel(ArticulationBody body)
    {
        if (body == null) return;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.jointFriction = 0f;
        body.sleepThreshold = 0f;
        
        var drive = body.xDrive;
        drive.stiffness = 0;
        drive.damping = driveDamping; 
        drive.forceLimit = driveForceLimit;
        body.xDrive = drive;
    }

    void OnLeftWheelVelReceived(Float64Msg msg)
    {
        float targetDegPerSec = (float)msg.data * Mathf.Rad2Deg;
        if (invertLeftWheel) targetDegPerSec = -targetDegPerSec;
        ApplyVelocity(wheelLeftJoint, targetDegPerSec);
    }

    void OnRightWheelVelReceived(Float64Msg msg)
    {
        float targetDegPerSec = (float)msg.data * Mathf.Rad2Deg;
        if (invertRightWheel) targetDegPerSec = -targetDegPerSec;
        ApplyVelocity(wheelRightJoint, targetDegPerSec);
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