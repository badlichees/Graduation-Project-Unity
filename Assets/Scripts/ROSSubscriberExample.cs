using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class ROSSubscriberExample : MonoBehaviour
{
    [SerializeField] private string topicName = "unity_listener";

    void Start()
    {
        // 订阅 ROS2 话题，收到消息自动调用 OnMessageReceived
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>(topicName, OnMessageReceived);

        Debug.Log($"已订阅 ROS2 话题：{topicName}");
    }

    private void OnMessageReceived(StringMsg msg)
    {
        Debug.Log($"收到 ROS2 消息：{msg.data}");
    }
}
