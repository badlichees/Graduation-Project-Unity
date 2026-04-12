using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System;

/// <summary>
/// 模拟TurtleBot3的IMU传感器，发布sensor_msgs/Imu到ROS2的/imu话题
/// 从imu_link的ArticulationBody读取角速度和线速度，并计算方向四元数
/// </summary>
public class ImuPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/imu";
    public float publishFrequency = 100.0f; // Hz

    [Header("IMU Sensor Link")]
    public ArticulationBody imuLink; // 指向imu_link的ArticulationBody

    [Header("TF Frame")]
    public string frameId = "imu_link";

    [Header("Noise Simulation (optional)")]
    public bool addNoise = false;
    public float angularVelocityNoiseStd = 0.01f; // 弧度/秒
    public float linearAccelerationNoiseStd = 0.02f; // 米/秒^2

    private ROSConnection ros;
    private float timeElapsed;
    private ImuMsg imuMsg;
    private HeaderMsg header;

    // 用于计算线性加速度的上一次速度
    private Vector3 lastVelocity = Vector3.zero;
    private bool firstUpdate = true;

    void Start()
    {
        // 如果没有指定imuLink，尝试从当前GameObject获取
        if (imuLink == null)
            imuLink = GetComponent<ArticulationBody>();

        if (imuLink == null)
        {
            Debug.LogError("ImuPublisher: No ArticulationBody assigned and none found on GameObject. Disabling.");
            enabled = false;
            return;
        }

        // 初始化ROS连接
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<ImuMsg>(topicName);

        // 初始化消息结构
        header = new HeaderMsg();
        header.frame_id = frameId;

        imuMsg = new ImuMsg();
        imuMsg.header = header;
        // 方向四元数（初始化为单位四元数）
        imuMsg.orientation = new RosMessageTypes.Geometry.QuaternionMsg { x = 0, y = 0, z = 0, w = 1 };
        // 方向协方差：[0]=-1 表示未提供可靠方向数据，Nav2/EKF 将忽略此字段
        imuMsg.orientation_covariance = new double[9] { -1, 0, 0, 0, 0, 0, 0, 0, 0 };
        // 角速度协方差（-1表示未知）
        imuMsg.angular_velocity_covariance = new double[9] { -1, 0, 0, 0, 0, 0, 0, 0, 0 };
        // 线性加速度协方差（-1表示未知）
        imuMsg.linear_acceleration_covariance = new double[9] { -1, 0, 0, 0, 0, 0, 0, 0, 0 };

        Debug.Log($"ImuPublisher initialized: publishing {frameId} at {publishFrequency}Hz");
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;
        float interval = 1.0f / publishFrequency;

        if (timeElapsed >= interval)
        {
            UpdateImuData(interval);
            PublishImu();
            timeElapsed = 0;
        }
    }

    /// <summary>
    /// 更新IMU数据：方向、角速度、线性加速度
    /// </summary>
    void UpdateImuData(float dt)
    {
        // 更新时间戳
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        header.stamp = new TimeMsg { sec = sec, nanosec = nanosec };

        // 方向：使用imu_link的世界旋转（转换为ROS四元数）
        Quaternion worldRot = imuLink.transform.rotation;
        imuMsg.orientation = worldRot.ToRosQuaternion();

        // 角速度：世界坐标系下的角速度，转换为局部坐标系（imu_link）
        Vector3 angularVelWorld = imuLink.angularVelocity;
        Vector3 angularVelLocal = imuLink.transform.InverseTransformDirection(angularVelWorld);

        // 线性加速度：通过速度差分计算（世界坐标系下），然后转换为局部坐标系
        Vector3 linearAccelWorld = Vector3.zero;
        if (!firstUpdate)
        {
            linearAccelWorld = (imuLink.velocity - lastVelocity) / dt;
        }
        else
        {
            firstUpdate = false;
        }
        lastVelocity = imuLink.velocity;
        Vector3 linearAccelLocal = imuLink.transform.InverseTransformDirection(linearAccelWorld);

        // 添加噪声（可选）
        if (addNoise)
        {
            angularVelLocal += new Vector3(
                GaussianNoise(0, angularVelocityNoiseStd),
                GaussianNoise(0, angularVelocityNoiseStd),
                GaussianNoise(0, angularVelocityNoiseStd)
            );
            linearAccelLocal += new Vector3(
                GaussianNoise(0, linearAccelerationNoiseStd),
                GaussianNoise(0, linearAccelerationNoiseStd),
                GaussianNoise(0, linearAccelerationNoiseStd)
            );
        }

        // 赋值给消息（注意ROS坐标系：x向前，y向左，z向上）
        // Unity坐标系：z向前（车头），y向上，x向右
        // 因此需要进行映射：Unity -> ROS
        // Unity (z, x, y) -> ROS (x, -y, z)
        imuMsg.angular_velocity.x = angularVelLocal.z;
        imuMsg.angular_velocity.y = -angularVelLocal.x;
        imuMsg.angular_velocity.z = angularVelLocal.y;

        imuMsg.linear_acceleration.x = linearAccelLocal.z;
        imuMsg.linear_acceleration.y = -linearAccelLocal.x;
        imuMsg.linear_acceleration.z = linearAccelLocal.y;

        // 重力补偿：静止时加速度计测量到 +9.81 m/s² 指向ROS的z轴（向上）
        float gravity = 9.81f;
        imuMsg.linear_acceleration.z += gravity;
    }

    /// <summary>
    /// 发布IMU消息到ROS
    /// </summary>
    void PublishImu()
    {
        ros.Publish(topicName, imuMsg);
    }

    /// <summary>
    /// 生成高斯噪声（均值为0，标准差为std）
    /// </summary>
    float GaussianNoise(float mean, float std)
    {
        float u1 = 1.0f - UnityEngine.Random.value;
        float u2 = 1.0f - UnityEngine.Random.value;
        float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
        return mean + std * randStdNormal;
    }

    /// <summary>
    /// 在Inspector中重置frameId到默认值
    /// </summary>
    void Reset()
    {
        frameId = "imu_link";
    }
}