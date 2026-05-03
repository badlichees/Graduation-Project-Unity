using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using RosMessageTypes.BuiltinInterfaces;
using System;
using RobotSimulation.MapGeneration;

public class OccupancyGridPublisher : MonoBehaviour
{
    [Header("ROS Settings")]
    public string topicName = "/map_raw";
    public string frameId = "map";
    [Range(0.1f, 10f)]
    public float publishFrequency = 1.0f;
    [Min(1)]
    public int publisherQueueSize = 10;

    [Header("Map Source")]
    public MapGenerator mapGenerator;

    [Header("Options")]
    [Tooltip("启动时自动读取地图")]
    public bool autoUpdateOnStart = true;
    [Tooltip("发布到 ROS 的栅格分辨率")]
    [Min(0.01f)]
    public float rosCellSize = 0.1f;

    private ROSConnection ros;
    private float timeElapsed;
    private OccupancyGridMsg cachedMsg;
    private bool hasMap = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<OccupancyGridMsg>(topicName, publisherQueueSize);

        if (mapGenerator == null)
            mapGenerator = GetComponent<MapGenerator>();

        if (mapGenerator == null)
        {
            Debug.LogError("OccupancyGridPublisher: 未找到 MapGenerator，请在 Inspector 中指定");
            enabled = false;
            return;
        }

        mapGenerator.OnMapGenerated += UpdateMap;

        if (autoUpdateOnStart && mapGenerator.GeneratedObstacleMap != null)
            UpdateMap();
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

    public void UpdateMap()
    {
        bool[,] obstacleMap = mapGenerator.GeneratedObstacleMap;
        if (obstacleMap == null)
        {
            Debug.LogWarning("OccupancyGridPublisher: MapGenerator 还未生成地图，UpdateMap() 无效");
            return;
        }

        int unityW = mapGenerator.MapSize.x;
        int unityH = mapGenerator.MapSize.y;
        float tileSize = mapGenerator.tileSize;
        int cellsPerTile = Mathf.Max(1, Mathf.RoundToInt(tileSize / Mathf.Max(rosCellSize, 0.01f)));
        float res = tileSize / cellsPerTile;

        // ROS 栅格轴向来自 Unity XZ 平面的旋转映射，宽高和原点不能直接照抄 Unity 网格
        uint rosWidth  = (uint)(unityH * cellsPerTile);
        uint rosHeight = (uint)(unityW * cellsPerTile);

        double originX = -unityH * tileSize / 2.0;
        double originY = -unityW * tileSize / 2.0;

        sbyte[] data = new sbyte[rosWidth * rosHeight];
        for (int gx = 0; gx < unityW; gx++)
        {
            for (int gy = 0; gy < unityH; gy++)
            {
                sbyte cellValue = obstacleMap[gx, gy] ? (sbyte)100 : (sbyte)0;

                for (int subX = 0; subX < cellsPerTile; subX++)
                {
                    // 翻转 Unity X 轴，让 ROS map 与 odom/goal 的坐标约定一致
                    int ry = (unityW - 1 - gx) * cellsPerTile + subX;
                    int rowStart = ry * (int)rosWidth;

                    for (int subY = 0; subY < cellsPerTile; subY++)
                    {
                        int rx = gy * cellsPerTile + subY;
                        data[rowStart + rx] = cellValue;
                    }
                }
            }
        }

        var stamp = MakeStamp();
        cachedMsg = new OccupancyGridMsg
        {
            header = new HeaderMsg { stamp = stamp, frame_id = frameId },
            info = new MapMetaDataMsg
            {
                map_load_time = stamp,
                resolution = res,
                width = rosWidth,
                height = rosHeight,
                origin = new PoseMsg
                {
                    position = new PointMsg { x = originX, y = originY, z = 0.0 },
                    orientation = new QuaternionMsg { x = 0, y = 0, z = 0, w = 1 }
                }
            },
            data = data
        };

        hasMap = true;
        Debug.Log($"OccupancyGridPublisher: 地图已构建 " +
                  $"[ROS {rosWidth}×{rosHeight}，分辨率={res}m，cellsPerTile={cellsPerTile}，" +
                  $"原点=({originX:F2}, {originY:F2})]");

        PublishMap();
    }

    void PublishMap()
    {
        if (cachedMsg == null) return;
        cachedMsg.header.stamp = MakeStamp();
        ros.Publish(topicName, cachedMsg);
    }

    static TimeMsg MakeStamp()
    {
        var now = DateTime.UtcNow;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ts = now - epoch;
        int sec = (int)ts.TotalSeconds;
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
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x * res, 0.05f, size.y * res));
    }
}
