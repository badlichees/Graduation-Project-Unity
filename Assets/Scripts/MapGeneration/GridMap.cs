using UnityEngine;
using System;

namespace RobotSimulation.MapGeneration
{
    /// <summary>
    /// 网格地图表示，用于路径规划与ROS集成
    /// </summary>
    [System.Serializable]
    public class GridMap
    {
        public int width;               // 网格宽度（单元格数）
        public int height;              // 网格高度（单元格数）
        public float resolution;        // 每个单元格的尺寸（米）
        public Vector3 origin;          // 网格原点在世界坐标系中的位置（左下角）
        public byte[,] data;            // 占用数据：0-100表示占用概率，255表示未知
        
        /// <summary>
        /// 从障碍物布尔网格创建GridMap
        /// </summary>
        /// <param name="obstacleGrid">true表示障碍物</param>
        /// <param name="resolution">单元格尺寸（米）</param>
        /// <param name="origin">网格原点（世界坐标，默认左下角）</param>
        public GridMap(bool[,] obstacleGrid, float resolution, Vector3 origin = default)
        {
            width = obstacleGrid.GetLength(0);
            height = obstacleGrid.GetLength(1);
            this.resolution = resolution;
            this.origin = origin;
            data = new byte[width, height];
            
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    data[x, y] = obstacleGrid[x, y] ? (byte)100 : (byte)0;
                }
            }
        }
        
        /// <summary>
        /// 从占用概率数组创建GridMap
        /// </summary>
        public GridMap(byte[,] occupancyData, float resolution, Vector3 origin)
        {
            width = occupancyData.GetLength(0);
            height = occupancyData.GetLength(1);
            this.resolution = resolution;
            this.origin = origin;
            data = occupancyData;
        }
        
        /// <summary>
        /// 检查网格坐标是否在范围内
        /// </summary>
        public bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }
        
        /// <summary>
        /// 检查单元格是否被占用（占用值 >= 阈值）
        /// </summary>
        public bool IsOccupied(int x, int y, byte threshold = 50)
        {
            if (!IsValidCoord(x, y)) return true; // 范围外视为障碍物
            return data[x, y] >= threshold;
        }
        
        /// <summary>
        /// 检查世界坐标点是否被占用
        /// </summary>
        public bool IsWorldPositionOccupied(Vector3 worldPos, byte threshold = 50)
        {
            Coord gridCoord = WorldToGrid(worldPos);
            return IsOccupied(gridCoord.x, gridCoord.y, threshold);
        }
        
        /// <summary>
        /// 将世界坐标转换为网格坐标
        /// </summary>
        public Coord WorldToGrid(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x - origin.x) / resolution);
            int y = Mathf.FloorToInt((worldPos.z - origin.z) / resolution); // 使用Z轴作为平面Y
            x = Mathf.Clamp(x, 0, width - 1);
            y = Mathf.Clamp(y, 0, height - 1);
            return new Coord(x, y);
        }
        
        /// <summary>
        /// 将网格坐标转换为世界坐标（返回单元格中心点）
        /// </summary>
        public Vector3 GridToWorld(int x, int y)
        {
            float worldX = origin.x + (x + 0.5f) * resolution;
            float worldZ = origin.z + (y + 0.5f) * resolution;
            return new Vector3(worldX, origin.y, worldZ);
        }
        
        /// <summary>
        /// 将网格坐标转换为世界坐标（使用Coord）
        /// </summary>
        public Vector3 GridToWorld(Coord coord)
        {
            return GridToWorld(coord.x, coord.y);
        }
        
        /// <summary>
        /// 获取地图边界（世界坐标）
        /// </summary>
        public Bounds GetWorldBounds()
        {
            Vector3 min = origin;
            Vector3 max = origin + new Vector3(width * resolution, 0, height * resolution);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = new Vector3(width * resolution, 1f, height * resolution);
            return new Bounds(center, size);
        }
        
        /// <summary>
        /// 创建深拷贝
        /// </summary>
        public GridMap Clone()
        {
            byte[,] clonedData = (byte[,])data.Clone();
            return new GridMap(clonedData, resolution, origin);
        }
        
        /// <summary>
        /// 将地图数据转换为ROS OccupancyGrid消息所需的sbyte数组（-1表示未知）
        /// </summary>
        public sbyte[] ToROSOccupancyGrid()
        {
            sbyte[] rosData = new sbyte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte val = data[x, y];
                    // 转换：0-100 -> 0-100，未知（255）-> -1
                    rosData[y * width + x] = val == 255 ? (sbyte)-1 : (sbyte)val;
                }
            }
            return rosData;
        }
        
        /// <summary>
        /// 从ROS OccupancyGrid消息导入数据
        /// </summary>
        public static GridMap FromROSOccupancyGrid(sbyte[] rosData, int width, int height, float resolution, Vector3 origin)
        {
            byte[,] gridData = new byte[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    sbyte val = rosData[y * width + x];
                    gridData[x, y] = val < 0 ? (byte)255 : (byte)val;
                }
            }
            return new GridMap(gridData, resolution, origin);
        }
        
        /// <summary>
        /// 可视化调试：在Scene视图中绘制网格
        /// </summary>
        public void DrawDebugGizmos(float duration = 0, bool drawOccupied = true, bool drawFree = false)
        {
            Color occupiedColor = new Color(1, 0, 0, 0.3f);   // 半透明红色
            Color freeColor = new Color(0, 1, 0, 0.1f);       // 半透明绿色
            
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector3 centre = GridToWorld(x, y);
                    bool occupied = IsOccupied(x, y);
                    
                    if (occupied && drawOccupied)
                    {
                        Gizmos.color = occupiedColor;
                        Gizmos.DrawCube(centre, new Vector3(resolution * 0.9f, 0.1f, resolution * 0.9f));
                    }
                    else if (!occupied && drawFree)
                    {
                        Gizmos.color = freeColor;
                        Gizmos.DrawWireCube(centre, new Vector3(resolution, 0.1f, resolution));
                    }
                }
            }
        }
    }
}