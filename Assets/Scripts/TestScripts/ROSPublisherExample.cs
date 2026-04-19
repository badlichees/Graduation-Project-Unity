using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

/// <summary>
/// Unity与ROS2通信示例类，用于Unity向ROS2发布字符串消息
/// </summary>
public class ROSPublisherExample : MonoBehaviour
{
    ROSConnection ros;

    public string topicName = "unity_talker";
    public float publisherMessageFrequency = 1.0f; // 发布频率
    private float timeElapsed;

    /// <summary>
    /// 在启动时获取ROS连接的实例，并注册类型为StringMsg的话题，为发送消息做准备
    /// </summary>
    void Start()
    {        
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(topicName);
    }

    /// <summary>
    /// 每帧将timeElapsed累加，当超过发布频率规定的间隔时间时，创建消息并发布
    /// </summary>
    void Update()
    {
        timeElapsed += Time.deltaTime;

        if (timeElapsed > publisherMessageFrequency)
        {
            StringMsg stringMsg = new StringMsg("Hello ROS2, this is Unity!"); // 创建消息

            ros.Publish(topicName, stringMsg); // 发送消息

            timeElapsed = 0; // 重新计算间隔时间
        }
    }
}
