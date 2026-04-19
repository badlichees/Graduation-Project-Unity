using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System;

/// <summary>
/// 发布 IMU 数据。
/// </summary>
public class ImuPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/imu";
    public float publishFrequency = 30.0f; // Hz
    [Min(1)]
    public int publisherQueueSize = 100;

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

    private Vector3 lastVelocity = Vector3.zero;
    private bool firstUpdate = true;

    void Start()
    {
        if (imuLink == null)
            imuLink = GetComponent<ArticulationBody>();

        if (imuLink == null)
        {
            Debug.LogError("ImuPublisher: No ArticulationBody assigned and none found on GameObject. Disabling.");
            enabled = false;
            return;
        }

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<ImuMsg>(topicName, publisherQueueSize);

        header = new HeaderMsg();
        header.frame_id = frameId;

        imuMsg = new ImuMsg();
        imuMsg.header = header;
        imuMsg.orientation = new RosMessageTypes.Geometry.QuaternionMsg { x = 0, y = 0, z = 0, w = 1 };
        imuMsg.orientation_covariance = new double[9] { -1, 0, 0, 0, 0, 0, 0, 0, 0 };
        imuMsg.angular_velocity_covariance = new double[9] { -1, 0, 0, 0, 0, 0, 0, 0, 0 };
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
    /// 更新 IMU 数据。
    /// </summary>
    void UpdateImuData(float dt)
    {
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        header.stamp = new TimeMsg { sec = sec, nanosec = nanosec };

        Quaternion worldRot = imuLink.transform.rotation;
        imuMsg.orientation = worldRot.ToRosQuaternion();

        Vector3 angularVelWorld = imuLink.angularVelocity;
        Vector3 angularVelLocal = imuLink.transform.InverseTransformDirection(angularVelWorld);

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

        imuMsg.angular_velocity.x = angularVelLocal.z;
        imuMsg.angular_velocity.y = -angularVelLocal.x;
        imuMsg.angular_velocity.z = angularVelLocal.y;

        imuMsg.linear_acceleration.x = linearAccelLocal.z;
        imuMsg.linear_acceleration.y = -linearAccelLocal.x;
        imuMsg.linear_acceleration.z = linearAccelLocal.y;

        float gravity = 9.81f;
        imuMsg.linear_acceleration.z += gravity;
    }

    /// <summary>
    /// 发布 IMU 消息。
    /// </summary>
    void PublishImu()
    {
        ros.Publish(topicName, imuMsg);
    }

    float GaussianNoise(float mean, float std)
    {
        float u1 = 1.0f - UnityEngine.Random.value;
        float u2 = 1.0f - UnityEngine.Random.value;
        float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
        return mean + std * randStdNormal;
    }

    void Reset()
    {
        frameId = "imu_link";
    }
}
