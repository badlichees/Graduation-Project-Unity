using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using System;

public class ROSPublisherExample : MonoBehaviour 
{
    ROSConnection ros;
    public string topicName = "unity_talker";

    // 发布频率
    public float publisherMessageFrequency = 1.0f;
    private float timeElapsed;

    void Start()
    {        
        ros = ROSConnection.GetOrCreateInstance(); // 获取 ROS 连接实例
        ros.RegisterPublisher<StringMsg>(topicName); // 注册话题，告诉 ROS 我们要发什么类型的消息
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;

        if (timeElapsed > publisherMessageFrequency)
        {
            // 创建消息
            StringMsg stringMsg = new StringMsg("Hello ROS2, this is Unity!");

            // 发送消息
            ros.Publish(topicName, stringMsg);

            timeElapsed = 0;
        }
    }
}
