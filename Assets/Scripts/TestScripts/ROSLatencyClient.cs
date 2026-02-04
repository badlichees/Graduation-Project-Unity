using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

/// <summary>
/// Unity-ROS2通信延迟测试客户端：通过发送带时间戳的ping消息然后接收ROS2返回的pong消息计算时间差，用于测量Unity与ROS2之间的通信延迟
/// 依赖ROS-TCP-Connector，需确保端点正确配置
/// </summary>
public class ROSLatencyClient : MonoBehaviour
{
    ROSConnection ros; // 实例化ROS连接管理器
    public float testInterval = 1.0f; // 测试间隔（秒）
    public float pongTimeout = 5.0f;  // pong响应超时时间（秒）
    public float initialDelay = 2.0f; // 初始延迟（秒），等待连接稳定
    public bool enableTesting = true; // 启用测试标记
    
    private float lastPingTime; // 上次ping的时间戳
    private int sequenceNumber = 0; // ping消息序列号
    private bool waitingForPong = false;
    private float pongWaitStartTime; // pong等待开始所经过的时间
    private string clientId = "UnityClient"; // 客户端标识
    private bool subscribed = false; // 防止重复订阅
    private int lastReceivedSequence = -1; // 上次收到的pong序列号
    
    /// <summary>
    /// 启动时初始化ROS连接并订阅pong主题
    /// </summary>
    void Start()
    {
        // 若测试被禁用，则不执行任何初始化操作
        if (!enableTesting)
        {
            Debug.LogWarning($"[{clientId}] Latency testing is disabled.");
            return;
        }
        
        ros = ROSConnection.GetOrCreateInstance(); // 获取ROS连接实例
        ros.RegisterPublisher<Float32MultiArrayMsg>("ping"); // 注册发布者，用于发送ping消息
        
        // 订阅pong主题（仅一次），同时注册回调函数处理接收到的pong消息
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
    
    /// <summary>
    /// 每帧检查pong响应是否超时，如果超时则重置等待状态
    /// </summary>
    void Update()
    {
        if (waitingForPong && (Time.realtimeSinceStartup - pongWaitStartTime) > pongTimeout)
        {
            int timedOutSeq = sequenceNumber - 1; // 上次发送的ping序列号
            if (timedOutSeq < 0) timedOutSeq = 0;
            Debug.LogWarning($"[{clientId}] Pong timeout for seq={timedOutSeq}, resetting.");
            waitingForPong = false;
        }
    }
    
    void SendPing()
    {
        if (!enableTesting) return;
        
        if (!waitingForPong)
        {
            lastPingTime = Time.realtimeSinceStartup;
            waitingForPong = true;
            pongWaitStartTime = lastPingTime;
            
            // 发送ping消息，包含时间戳和序列号
            var pingMsg = new Float32MultiArrayMsg();
            pingMsg.data = new float[] { lastPingTime, sequenceNumber };            
            ros.Publish("ping", pingMsg);
            
            // 记录下序列号和时间戳（精确到三位小数）并递增序列号
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
        
        // 安全性长度检查，确保数据长度至少为2
        if (msg.data.Length >= 2)
        {
            // 获取ping消息的时间戳和序列号
            float receivedPingTime = msg.data[0];
            int sequence = (int)msg.data[1];
            
            // 忽略相同序列号的重复消息
            if (sequence == lastReceivedSequence)
            {
                Debug.Log($"[{clientId}] Ignoring duplicate pong seq={sequence}");
                return;
            }
            lastReceivedSequence = sequence;
            
            // 计算往返时间
            float currentTime = Time.realtimeSinceStartup;
            float roundTripTime = (currentTime - receivedPingTime) * 1000; // 转换为毫秒
            
            // 记录结果并重置等待状态
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