using UnityEngine;
using System;

namespace RobotSimulation.MapGeneration
{
    // 保留独立数据模型，便于测试坐标换算而不依赖场景中的 MapGenerator
    [System.Serializable]
    public class GridMap
    {
        public int width;
        public int height;
        public float resolution;
        public Vector3 origin;
        public byte[,] data;

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
        
        public GridMap(byte[,] occupancyData, float resolution, Vector3 origin)
        {
            width = occupancyData.GetLength(0);
            height = occupancyData.GetLength(1);
            this.resolution = resolution;
            this.origin = origin;
            data = occupancyData;
        }
        
        public bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }
        
        public bool IsOccupied(int x, int y, byte threshold = 50)
        {
            // 范围外按障碍物处理，调用方就不需要额外做边界保护
            if (!IsValidCoord(x, y)) return true;
            return data[x, y] >= threshold;
        }

        public bool IsWorldPositionOccupied(Vector3 worldPos, byte threshold = 50)
        {
            Coord gridCoord = WorldToGrid(worldPos);
            return IsOccupied(gridCoord.x, gridCoord.y, threshold);
        }
        
        // Unity 地面坐标使用 XZ 平面，网格内部仍用传统二维 x/y
        public Coord WorldToGrid(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x - origin.x) / resolution);
            int y = Mathf.FloorToInt((worldPos.z - origin.z) / resolution);
            x = Mathf.Clamp(x, 0, width - 1);
            y = Mathf.Clamp(y, 0, height - 1);
            return new Coord(x, y);
        }
        
        public Vector3 GridToWorld(int x, int y)
        {
            float worldX = origin.x + (x + 0.5f) * resolution;
            float worldZ = origin.z + (y + 0.5f) * resolution;
            return new Vector3(worldX, origin.y, worldZ);
        }
        
        public Vector3 GridToWorld(Coord coord)
        {
            return GridToWorld(coord.x, coord.y);
        }
        
        public Bounds GetWorldBounds()
        {
            Vector3 min = origin;
            Vector3 max = origin + new Vector3(width * resolution, 0, height * resolution);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = new Vector3(width * resolution, 1f, height * resolution);
            return new Bounds(center, size);
        }
        
        public GridMap Clone()
        {
            byte[,] clonedData = (byte[,])data.Clone();
            return new GridMap(clonedData, resolution, origin);
        }
        
        // ROS OccupancyGrid 使用一维行优先数组，并用 -1 表示未知
        public sbyte[] ToROSOccupancyGrid()
        {
            sbyte[] rosData = new sbyte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte val = data[x, y];
                    rosData[y * width + x] = val == 255 ? (sbyte)-1 : (sbyte)val;
                }
            }
            return rosData;
        }
        
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
        
        public void DrawDebugGizmos(float duration = 0, bool drawOccupied = true, bool drawFree = false)
        {
            Color occupiedColor = new Color(1, 0, 0, 0.3f);
            Color freeColor = new Color(0, 1, 0, 0.1f);
            
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
