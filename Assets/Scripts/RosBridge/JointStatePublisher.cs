using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.BuiltinInterfaces;
using System;

public class JointStatePublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/joint_states";
    public float publishFrequency = 30.0f;
    [Min(1)]
    public int publisherQueueSize = 100;

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

    private float lastLeftAngle = 0.0f;
    private float lastRightAngle = 0.0f;
    private bool firstUpdate = true;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(topicName, publisherQueueSize);

        header = new HeaderMsg();
        header.frame_id = "";

        jointStateMsg = new JointStateMsg();
        jointStateMsg.header = header;
        jointStateMsg.name = new string[] { leftWheelJointName, rightWheelJointName };
        jointStateMsg.position = new double[2];
        jointStateMsg.velocity = new double[2];
        jointStateMsg.effort = new double[0];

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

    void UpdateJointStates()
    {
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timeSpan = now - epoch;
        int sec = (int)timeSpan.TotalSeconds;
        uint nanosec = (uint)((timeSpan.TotalSeconds - sec) * 1e9);
        header.stamp = new TimeMsg { sec = sec, nanosec = nanosec };

        float leftAngle = GetWheelAngle(wheelLeftJoint);
        float rightAngle = GetWheelAngle(wheelRightJoint);

        float dt = 1.0f / publishFrequency;
        float leftVel = 0.0f;
        float rightVel = 0.0f;

        if (!firstUpdate)
        {
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

    float GetWheelAngle(ArticulationBody wheel)
    {
        if (wheel == null)
            return 0.0f;

        return wheel.transform.localEulerAngles.x * Mathf.Deg2Rad;
    }

    void PublishJointStates()
    {
        ros.Publish(topicName, jointStateMsg);
    }

    void Reset()
    {
        leftWheelJointName = "wheel_left_joint";
        rightWheelJointName = "wheel_right_joint";
    }
}
