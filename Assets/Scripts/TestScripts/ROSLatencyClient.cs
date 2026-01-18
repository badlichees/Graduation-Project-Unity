/*
Unity-ROS2 通信延迟测试客户端 (ROSLatencyClient)

功能：测量Unity与ROS2之间的通信往返延迟
原理：发送带时间戳的ping消息，接收ROS2返回的pong消息，计算时间差
配置：testInterval(测试间隔)、pongTimeout(超时时间)、initialDelay(初始延迟)
注意：依赖ROS TCP Connector，需确保ROS TCP端点正确配置
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using System;

public class ROSLatencyClient : MonoBehaviour
{
    ROSConnection ros;
    public float testInterval = 1.0f;          // 测试间隔（秒）
    public float pongTimeout = 5.0f;           // pong响应超时时间（秒）
    public float initialDelay = 2.0f;          // 初始延迟（秒），等待连接稳定
    public bool enableTesting = true;          // 是否启用测试
    
    private float lastPingTime;
    private int sequenceNumber = 0;
    private bool waitingForPong = false;
    private float pongWaitStartTime;
    private string clientId = "UnityClient";   // 客户端标识
    private bool subscribed = false;           // 防止重复订阅
    private int lastReceivedSequence = -1;     // 去重：上次接收的序列号
    
    // Start is called before the first frame update
    void Start()
    {
        if (!enableTesting)
        {
            Debug.LogWarning($"[{clientId}] Latency testing is disabled.");
            return;
        }
        
        ros = ROSConnection.GetOrCreateInstance();
        
        // 注册发布者（必需，否则ROS TCP端点无法识别主题）
        ros.RegisterPublisher<Float32MultiArrayMsg>("ping");
        
        // 订阅pong主题（仅一次）
        if (!subscribed)
        {
            ros.Subscribe<Float32MultiArrayMsg>("pong", OnPongReceived);
            subscribed = true;
        }
        
        // 等待连接稳定后再开始发送ping
        Debug.Log($"[{clientId}] Latency test client initializing, waiting {initialDelay}s for connection stabilization...");
        
        // 开始定期发送ping（使用nameof避免字符串硬编码）
        InvokeRepeating(nameof(SendPing), initialDelay, testInterval);
        Debug.Log($"[{clientId}] Latency test client started, interval={testInterval}s, initial delay={initialDelay}s");
    }
    
    void Update()
    {
        // 超时检测
        if (waitingForPong && (Time.realtimeSinceStartup - pongWaitStartTime) > pongTimeout)
        {
            int timedOutSeq = sequenceNumber - 1;
            if (timedOutSeq < 0) timedOutSeq = 0;
            Debug.LogWarning($"[{clientId}] Pong timeout for seq={timedOutSeq}, resetting.");
            waitingForPong = false;
        }
    }
    
    void SendPing()
    {
        if (!enableTesting) return;
        
        if (!waitingForPong)  // 避免同时发送多个ping
        {
            lastPingTime = Time.realtimeSinceStartup;
            waitingForPong = true;
            pongWaitStartTime = lastPingTime;
            
            // 发送ping消息，包含时间戳和序列号
            var pingMsg = new Float32MultiArrayMsg();
            pingMsg.data = new float[] { lastPingTime, sequenceNumber };
            
            ros.Publish("ping", pingMsg);
            
            Debug.Log($"[{clientId}] Sent ping: seq={sequenceNumber}, time={lastPingTime:F3}");
            sequenceNumber++;
        }
        else
        {
            int waitingSeq = sequenceNumber - 1;
            if (waitingSeq < 0) waitingSeq = 0;
            Debug.LogWarning($"[{clientId}] Skipping ping: still waiting for pong seq={waitingSeq}");
        }
    }
    
    
    void OnPongReceived(Float32MultiArrayMsg msg)
    {
        if (!enableTesting) return;
        
        if (msg.data.Length >= 2)
        {
            float receivedPingTime = msg.data[0];
            int sequence = (int)msg.data[1];
            
            // 忽略相同序列号的重复消息
            if (sequence == lastReceivedSequence)
            {
                Debug.Log($"[{clientId}] Ignoring duplicate pong seq={sequence}");
                return;
            }
            lastReceivedSequence = sequence;
            
            float currentTime = Time.realtimeSinceStartup;
            float roundTripTime = (currentTime - receivedPingTime) * 1000;  // 转换为毫秒
            
            Debug.Log($"[{clientId}] Received pong: seq={sequence}, round-trip time={roundTripTime:F3}ms");
            waitingForPong = false;
        }
        else
        {
            Debug.LogWarning($"[{clientId}] Received invalid pong message");
        }
    }
    
    void OnDestroy()
    {
        // 取消定时发送
        CancelInvoke(nameof(SendPing));
        
        // 取消订阅以避免内存泄漏
        if (ros != null)
        {
            ros.Unsubscribe("pong");
        }
    }
}