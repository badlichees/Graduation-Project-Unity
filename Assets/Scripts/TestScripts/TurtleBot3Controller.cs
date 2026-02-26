using UnityEngine;

public class TurtleBotDifferentialDriveController : MonoBehaviour
{
    [Header("=== Wheels ===")]
    public ArticulationBody leftWheel;
    public ArticulationBody rightWheel;

    [Header("=== 速度指令（推荐固定 0.3~0.4）===")]
    public float maxLinearSpeed = 0.35f;     // m/s   ← 真实TurtleBot3大概0.22，这里可以稍微高一点
    public float maxAngularSpeed = 1.5f;     // rad/s

    [Header("=== 关键可调参数（重点调这几个）===")]
    [Range(5000f, 50000f)] public float wheelDamping = 12000f;
    [Range(10000f, 1000000f)] public float wheelForceLimit = 450000f;  // ← 最重要！调这个解决空转/无力
    public DriveAxis wheelDriveAxis = DriveAxis.X;   // 先试 X，不行改 Z

    [Header("=== 模型参数（一般不用改）===")]
    public float wheelRadius = 0.033f;
    public float trackWidth = 0.16f;

    [Header("=== Debug ===")]
    public bool showDebugLog = true;

    public enum DriveAxis { X, Y, Z }

    void Start()
    {
        if (leftWheel == null) leftWheel = transform.Find("base_link/wheel_left_link")?.GetComponent<ArticulationBody>();
        if (rightWheel == null) rightWheel = transform.Find("base_link/wheel_right_link")?.GetComponent<ArticulationBody>();

        SetupWheel(leftWheel);
        SetupWheel(rightWheel);

        Debug.Log("✅ 可调版控制器已启动！Play模式下直接拖Inspector滑块实时调手感");
    }

    void Update()
    {
        float forward = Input.GetAxis("Vertical");
        float turn = -Input.GetAxis("Horizontal");

        float linear = forward * maxLinearSpeed;
        float angular = turn * maxAngularSpeed;

        if (Input.GetKey(KeyCode.Space)) linear = angular = 0f;

        SetTargetVelocity(linear, angular);
    }

    public void SetTargetVelocity(float linear, float angular)
    {
        float leftVelRad = (linear - angular * trackWidth / 2f) / wheelRadius * Mathf.Rad2Deg;
        float rightVelRad = (linear + angular * trackWidth / 2f) / wheelRadius * Mathf.Rad2Deg;

        SetWheelVelocity(leftWheel, leftVelRad);
        SetWheelVelocity(rightWheel, rightVelRad);

        if (showDebugLog)
            Debug.Log($"指令: {linear:F2} m/s | 左轮:{leftVelRad:F0}°/s 右轮:{rightVelRad:F0}°/s");
    }

    private void SetupWheel(ArticulationBody wheel)
    {
        var drive = GetDrive(wheel);
        drive.stiffness = 0f;
        drive.damping = wheelDamping;
        drive.forceLimit = wheelForceLimit;
        SetDrive(wheel, drive);
    }

    private ArticulationDrive GetDrive(ArticulationBody body)
    {
        return wheelDriveAxis switch
        {
            DriveAxis.X => body.xDrive,
            DriveAxis.Y => body.yDrive,
            DriveAxis.Z => body.zDrive,
            _ => body.xDrive
        };
    }

    private void SetDrive(ArticulationBody body, ArticulationDrive drive)
    {
        switch (wheelDriveAxis)
        {
            case DriveAxis.X: body.xDrive = drive; break;
            case DriveAxis.Y: body.yDrive = drive; break;
            case DriveAxis.Z: body.zDrive = drive; break;
        }
    }

    private void SetWheelVelocity(ArticulationBody wheel, float targetDegPerSec)
    {
        var drive = GetDrive(wheel);
        drive.targetVelocity = targetDegPerSec;
        SetDrive(wheel, drive);
    }
}