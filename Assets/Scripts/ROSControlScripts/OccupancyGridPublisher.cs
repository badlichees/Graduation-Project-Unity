using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using RosMessageTypes.BuiltinInterfaces;
using System;
using RobotSimulation.MapGeneration;

/// <summary>
/// 将 MapGenerator 生成的占用栅格地图发布到 ROS2 的 /map 话题（nav_msgs/OccupancyGrid）。
///
/// 坐标系转换：Unity(X右, Y上, Z前) → ROS(X前, Y左, Z上)
///   ROS width  方向 = Unity Z 方向（mapSize.y 个格子）
///   ROS height 方向 = Unity -X 方向（mapSize.x 个格子，Y轴取反）
///
/// 数据映射：Unity 格子 (gx, gy) → ROS 线性索引 = (unityW-1-gx)*width + gy
/// 地图原点（cell 0,0 左下角）：
///   ros_x = -mapSize.y/2 * tileSize
///   ros_y = -mapSize.x/2 * tileSize
/// </summary>
public class OccupancyGridPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/map";
    public string frameId = "map";
    [Range(0.1f, 10f)]
    public float publishFrequency = 1.0f; // Hz，静态地图 1Hz 足够

    [Header("Map Source")]
    public MapGenerator mapGenerator;

    [Header("Options")]
    [Tooltip("Start() 结束后自动读取 MapGenerator 数据（需要 MapGenerator.generateOnStart = true）")]
    public bool autoUpdateOnStart = true;

    private ROSConnection ros;
    private float timeElapsed;
    private OccupancyGridMsg cachedMsg;
    private bool hasMap = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<OccupancyGridMsg>(topicName);

        if (mapGenerator == null)
            mapGenerator = GetComponent<MapGenerator>();

        if (mapGenerator == null)
        {
            Debug.LogError("OccupancyGridPublisher: 未找到 MapGenerator，请在 Inspector 中指定。");
            enabled = false;
            return;
        }

        // 订阅地图生成事件，每次 GenerateMap() 完成后自动刷新
        mapGenerator.OnMapGenerated += UpdateMap;

        if (autoUpdateOnStart && mapGenerator.GeneratedObstacleMap != null)
        {
            // MapGenerator 在 Awake 或更早已生成好地图时直接使用
            UpdateMap();
        }
    }

    void OnDestroy()
    {
        if (mapGenerator != null)
            mapGenerator.OnMapGenerated -= UpdateMap;
    }

    void Update()
    {
        if (!hasMap) return;

        timeElapsed += Time.deltaTime;
        if (timeElapsed >= 1.0f / publishFrequency)
        {
            PublishMap();
            timeElapsed = 0;
        }
    }

    /// <summary>
    /// 从 MapGenerator 读取最新障碍物数据并构建 OccupancyGridMsg。
    /// 在 MapGenerator.GenerateMap() 完成后调用此方法。
    /// </summary>
    public void UpdateMap()
    {
        bool[,] obstacleMap = mapGenerator.GeneratedObstacleMap;
        if (obstacleMap == null)
        {
            Debug.LogWarning("OccupancyGridPublisher: MapGenerator 还未生成地图，UpdateMap() 无效。");
            return;
        }

        int unityW = mapGenerator.MapSize.x; // Unity X 方向格子数
        int unityH = mapGenerator.MapSize.y; // Unity Z 方向格子数
        float res   = mapGenerator.tileSize;

        // ROS 地图尺寸（见文件头注释）
        uint rosWidth  = (uint)unityH; // ROS X = Unity Z
        uint rosHeight = (uint)unityW; // ROS Y = -Unity X（取反）

        // 地图原点（OccupancyGrid cell(0,0) 的左下角，ROS 坐标）
        double originX = -unityH / 2.0 * res;
        double originY = -unityW / 2.0 * res;

        // 数据填充：Unity(gx, gy) → ROS index = (unityW-1-gx)*rosWidth + gy
        sbyte[] data = new sbyte[rosWidth * rosHeight];
        for (int gx = 0; gx < unityW; gx++)
        {
            int ry = unityW - 1 - gx;
            for (int gy = 0; gy < unityH; gy++)
            {
                int idx = ry * (int)rosWidth + gy;
                data[idx] = obstacleMap[gx, gy] ? (sbyte)100 : (sbyte)0;
            }
        }

        // 构建消息
        var stamp = MakeStamp();
        cachedMsg = new OccupancyGridMsg
        {
            header = new HeaderMsg { stamp = stamp, frame_id = frameId },
            info = new MapMetaDataMsg
            {
                map_load_time = stamp,
                resolution    = res,
                width         = rosWidth,
                height        = rosHeight,
                origin        = new PoseMsg
                {
                    position    = new PointMsg { x = originX, y = originY, z = 0.0 },
                    orientation = new QuaternionMsg { x = 0, y = 0, z = 0, w = 1 }
                }
            },
            data = data
        };

        hasMap = true;
        Debug.Log($"OccupancyGridPublisher: 地图已构建 " +
                  $"[ROS {rosWidth}×{rosHeight}，分辨率={res}m，" +
                  $"原点=({originX:F2}, {originY:F2})]");

        // 立即发布一次，不等下次 Update 定时
        PublishMap();
    }

    void PublishMap()
    {
        if (cachedMsg == null) return;
        cachedMsg.header.stamp = MakeStamp(); // 刷新时间戳
        ros.Publish(topicName, cachedMsg);
    }

    static TimeMsg MakeStamp()
    {
        var now   = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts    = now - epoch;
        int sec   = (int)ts.TotalSeconds;
        uint nsec = (uint)((ts.TotalSeconds - sec) * 1e9);
        return new TimeMsg { sec = sec, nanosec = nsec };
    }

    void OnDrawGizmosSelected()
    {
        if (mapGenerator == null) return;
        var size = mapGenerator.MapSize;
        if (size.x == 0 || size.y == 0) return;
        float res = mapGenerator.tileSize;
        Gizmos.color = Color.cyan;
        // 在场景视图中绘制地图边界（Unity 坐标，XZ 平面）
        Gizmos.DrawWireCube(Vector3.zero,
            new Vector3(size.x * res, 0.05f, size.y * res));
    }
}
