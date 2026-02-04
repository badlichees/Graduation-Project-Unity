using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

/// <summary>
/// Unity与ROS2通信示例类，用于Unity从ROS2订阅字符串消息
/// </summary>
public class ROSSubscriberExample : MonoBehaviour
{
    // 序列化字段，可在Inspector中设置话题名称
    [SerializeField] private string topicName = "unity_listener";

    /// <summary>
    /// 启动时订阅话题，收到消息自动调用OnMessageReceived
    /// </summary>
    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>(topicName, OnMessageReceived);
        Debug.Log($"已订阅 ROS2 话题：{topicName}");
    }

    /// <summary>
    /// 接收到消息时调用
    /// </summary>
    /// <param name="msg">接收到的字符串消息</param>
    private void OnMessageReceived(StringMsg msg)
    {
        Debug.Log($"收到 ROS2 消息：{msg.data}");
    }
}
