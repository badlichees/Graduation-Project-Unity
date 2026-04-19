using UnityEngine;
using System;
using System.Collections.Generic;

namespace RobotSimulation.MapGeneration
{
    [System.Serializable]
    public struct Coord
    {
        public int x;
        public int y;
        
        public Coord(int _x, int _y)
        {
            x = _x;
            y = _y;
        }
        
        public static bool operator ==(Coord c1, Coord c2)
        {
            return c1.x == c2.x && c1.y == c2.y;
        }
        
        public static bool operator !=(Coord c1, Coord c2)
        {
            return !(c1 == c2);
        }
        
        public static Coord operator +(Coord c1, Coord c2)
        {
            return new Coord(c1.x + c2.x, c1.y + c2.y);
        }
        
        public static Coord operator -(Coord c1, Coord c2)
        {
            return new Coord(c1.x - c2.x, c1.y - c2.y);
        }
        
        public static Coord operator *(Coord c, int multiplier)
        {
            return new Coord(c.x * multiplier, c.y * multiplier);
        }
        
        public static Coord operator *(int multiplier, Coord c)
        {
            return new Coord(c.x * multiplier, c.y * multiplier);
        }
        
        public static Coord operator /(Coord c, int divisor)
        {
            return new Coord(c.x / divisor, c.y / divisor);
        }
        
        public static implicit operator Vector2(Coord c)
        {
            return new Vector2(c.x, c.y);
        }
        
        public static implicit operator Vector3(Coord c)
        {
            return new Vector3(c.x, 0, c.y);
        }
        
        public float Distance(Coord other)
        {
            return Vector2.Distance(this, other);
        }
        
        public float SqrDistance(Coord other)
        {
            return (this - other).SqrMagnitude;
        }
        
        public float SqrMagnitude
        {
            get { return x * x + y * y; }
        }
        
        public override bool Equals(object obj)
        {
            if (obj is Coord)
            {
                return this == (Coord)obj;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2);
        }
        
        public override string ToString()
        {
            return string.Format("({0}, {1})", x, y);
        }
    }
    
    [System.Serializable]
    public class Map
    {
        public Coord mapSize;
        [Range(0,1)]
        public float obstaclePercent;
        public int seed;
        public float minObstacleHeight;
        public float maxObstacleHeight;
        public Color foregroundColour;
        public Color backgroundColour;
        
        public Coord mapCentre
        {
            get
            {
                return new Coord(mapSize.x/2, mapSize.y/2);
            }
        }
    }

    /// <summary>基于网格的程序化地图生成器。</summary>
    public class MapGenerator : MonoBehaviour
    {
        #region 公共字段
        
        public Map[] maps;
        public int mapIndex;
        
        public Transform tilePrefab;
        public Transform obstaclePrefab;
        
        [Range(0,1)]
        public float outlinePercent;
        
        public float tileSize = 1f;
        
        [Header("生成选项")]
        public bool generateOnStart = false;
        public bool instantiateObjects = true;

        /// <summary>每次 GenerateMap() 完成后触发，订阅者可在此刷新地图数据</summary>
        public event Action OnMapGenerated;

        #endregion
        
        #region 私有字段
        
        private List<Coord> allTileCoords;
        private Queue<Coord> shuffledTileCoords;
        private Queue<Coord> shuffledOpenTileCoords;
        private Transform[,] tileMap;
        private Map currentMap;
        
        #endregion
        
        #region Unity生命周期
        
        private void Start()
        {
            if (generateOnStart)
            {
                GenerateMap();
            }
        }
        
        #endregion
        
        #region 地图生成
        
        /// <summary>
        /// 生成地图，可选择是否实例化物体
        /// </summary>
        public void GenerateMap()
        {
            if (maps == null || maps.Length == 0)
            {
                Debug.LogError("未配置地图参数！");
                return;
            }
            
            if (mapIndex < 0 || mapIndex >= maps.Length)
            {
                Debug.LogWarning($"地图索引 {mapIndex} 超出范围，使用索引 0");
                mapIndex = 0;
            }
            
            currentMap = maps[mapIndex];
            tileMap = new Transform[currentMap.mapSize.x, currentMap.mapSize.y];
            System.Random prng = new System.Random(currentMap.seed);
            
            // 生成坐标
            allTileCoords = new List<Coord>();
            for (int x = 0; x < currentMap.mapSize.x; x++)
            {
                for (int y = 0; y < currentMap.mapSize.y; y++)
                {
                    allTileCoords.Add(new Coord(x, y));
                }
            }
            shuffledTileCoords = new Queue<Coord>(Utility.ShuffleArray(allTileCoords.ToArray(), currentMap.seed));
            
            // 创建地图容器对象
            string holderName = "Generated Map";
            Transform mapHolder = null;
            if (instantiateObjects)
            {
                if (transform.Find(holderName))
                {
                    DestroyImmediate(transform.Find(holderName).gameObject);
                }
                
                mapHolder = new GameObject(holderName).transform;
                mapHolder.parent = transform;
            }
            
            // 生成地板
            for (int x = 0; x < currentMap.mapSize.x; x++)
            {
                for (int y = 0; y < currentMap.mapSize.y; y++)
                {
                    Vector3 tilePosition = CoordToPosition(x, y);
                    if (instantiateObjects && tilePrefab != null)
                    {
                        Transform newTile = Instantiate(tilePrefab, tilePosition, Quaternion.Euler(Vector3.right * 90)) as Transform;
                        newTile.localScale = Vector3.one * (1 - outlinePercent) * tileSize;
                        newTile.parent = mapHolder;
                        tileMap[x, y] = newTile;
                    }
                }
            }
            
            // 生成障碍物
            bool[,] obstacleMap = new bool[currentMap.mapSize.x, currentMap.mapSize.y];
            
            int obstacleCount = (int)(currentMap.mapSize.x * currentMap.mapSize.y * currentMap.obstaclePercent);
            int currentObstacleCount = 0;
            List<Coord> allOpenCoords = new List<Coord>(allTileCoords);
            
            for (int i = 0; i < obstacleCount; i++)
            {
                Coord randomCoord = GetRandomCoord();
                obstacleMap[randomCoord.x, randomCoord.y] = true;
                currentObstacleCount++;
                
                if (randomCoord != currentMap.mapCentre && MapIsFullyAccessible(obstacleMap, currentObstacleCount))
                {
                    if (instantiateObjects && obstaclePrefab != null)
                    {
                        float obstacleHeight = Mathf.Lerp(currentMap.minObstacleHeight, currentMap.maxObstacleHeight, (float)prng.NextDouble());
                        Vector3 obstaclePosition = CoordToPosition(randomCoord.x, randomCoord.y);
                        
                        Transform newObstacle = Instantiate(obstaclePrefab, obstaclePosition + Vector3.up * obstacleHeight / 2, Quaternion.identity) as Transform;
                        newObstacle.parent = mapHolder;
                        newObstacle.localScale = new Vector3((1 - outlinePercent) * tileSize, obstacleHeight, (1 - outlinePercent) * tileSize);
                        
                        Renderer obstacleRenderer = newObstacle.GetComponent<Renderer>();
                        if (obstacleRenderer != null)
                        {
                            Material obstacleMaterial = new Material(obstacleRenderer.sharedMaterial);
                            float colourPercent = randomCoord.y / (float)currentMap.mapSize.y;
                            obstacleMaterial.color = Color.Lerp(currentMap.foregroundColour, currentMap.backgroundColour, colourPercent);
                            obstacleRenderer.sharedMaterial = obstacleMaterial;
                        }
                    }
                    
                    allOpenCoords.Remove(randomCoord);
                }
                else
                {
                    obstacleMap[randomCoord.x, randomCoord.y] = false;
                    currentObstacleCount--;
                }
            }
            
            shuffledOpenTileCoords = new Queue<Coord>(Utility.ShuffleArray(allOpenCoords.ToArray(), currentMap.seed));

            // 存储障碍物地图数据并通知订阅者
            GeneratedObstacleMap = obstacleMap;
            Debug.Log($"地图生成完成，尺寸：{currentMap.mapSize.x}x{currentMap.mapSize.y}，障碍物数量：{currentObstacleCount}");
            OnMapGenerated?.Invoke();
        }
        
        #endregion
        
        #region 地图验证和实用方法
        
        // 使用洪水填充算法验证地图连通性，确保玩家能到达所有区域
        private bool MapIsFullyAccessible(bool[,] obstacleMap, int currentObstacleCount)
        {
            // 初始化访问标记数组和BFS队列
            bool[,] mapFlags = new bool[obstacleMap.GetLength(0), obstacleMap.GetLength(1)];
            Queue<Coord> queue = new Queue<Coord>();
            queue.Enqueue(currentMap.mapCentre);
            mapFlags[currentMap.mapCentre.x, currentMap.mapCentre.y] = true;
            
            int accessibleTileCount = 1;
            
            while (queue.Count > 0)
            {
                Coord tile = queue.Dequeue();
                
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        int neighbourX = tile.x + x;
                        int neighbourY = tile.y + y;
                        if (x == 0 || y == 0)
                        {
                            if (neighbourX >= 0 && neighbourX < obstacleMap.GetLength(0) && neighbourY >= 0 && neighbourY < obstacleMap.GetLength(1))
                            {
                                if (!mapFlags[neighbourX, neighbourY] && !obstacleMap[neighbourX, neighbourY])
                                {
                                    mapFlags[neighbourX, neighbourY] = true;
                                    queue.Enqueue(new Coord(neighbourX, neighbourY));
                                    accessibleTileCount++;
                                }
                            }
                        }
                    }
                }
            }
            
            int targetAccessibleTileCount = (int)(currentMap.mapSize.x * currentMap.mapSize.y - currentObstacleCount);
            return targetAccessibleTileCount == accessibleTileCount;
        }
        
        private Vector3 CoordToPosition(int x, int y)
        {
            return new Vector3(-currentMap.mapSize.x / 2f + 0.5f + x, 0, -currentMap.mapSize.y / 2f + 0.5f + y) * tileSize;
        }
        
        public Transform GetTileFromPosition(Vector3 position)
        {
            int x = Mathf.RoundToInt(position.x / tileSize + (currentMap.mapSize.x - 1) / 2f);
            int y = Mathf.RoundToInt(position.z / tileSize + (currentMap.mapSize.y - 1) / 2f);
            x = Mathf.Clamp(x, 0, tileMap.GetLength(0) - 1);
            y = Mathf.Clamp(y, 0, tileMap.GetLength(1) - 1);
            return tileMap[x, y];
        }
        
        public Coord GetRandomCoord()
        {
            Coord randomCoord = shuffledTileCoords.Dequeue();
            shuffledTileCoords.Enqueue(randomCoord);
            return randomCoord;
        }
        
        public Transform GetRandomOpenTile()
        {
            Coord randomCoord = shuffledOpenTileCoords.Dequeue();
            shuffledOpenTileCoords.Enqueue(randomCoord);
            return tileMap[randomCoord.x, randomCoord.y];
        }
        
        #endregion
        
        #region 数据导出
        
        /// <summary>
        /// 获取生成的障碍物网格（true表示障碍物）
        /// </summary>
        public bool[,] GeneratedObstacleMap { get; private set; }
        
        /// <summary>
        /// 获取地图尺寸（网格单元数）
        /// </summary>
        public Coord MapSize
        {
            get { return currentMap != null ? currentMap.mapSize : new Coord(0, 0); }
        }
        
        /// <summary>
        /// 获取地图中心世界坐标
        /// </summary>
        public Vector3 MapCentreWorld
        {
            get { return CoordToPosition(MapSize.x / 2, MapSize.y / 2); }
        }
        
        /// <summary>
        /// 将世界坐标转换为网格坐标
        /// </summary>
        public Coord WorldToGrid(Vector3 worldPos)
        {
            int x = Mathf.RoundToInt(worldPos.x / tileSize + (MapSize.x - 1) / 2f);
            int y = Mathf.RoundToInt(worldPos.z / tileSize + (MapSize.y - 1) / 2f);
            x = Mathf.Clamp(x, 0, MapSize.x - 1);
            y = Mathf.Clamp(y, 0, MapSize.y - 1);
            return new Coord(x, y);
        }

        /// <summary>
        /// 判断网格是否在地图内且不是障碍物。
        /// </summary>
        public bool IsOpenTile(Coord gridCoord)
        {
            if (GeneratedObstacleMap == null)
                return false;

            if (gridCoord.x < 0 || gridCoord.x >= MapSize.x || gridCoord.y < 0 || gridCoord.y >= MapSize.y)
                return false;

            return !GeneratedObstacleMap[gridCoord.x, gridCoord.y];
        }

        /// <summary>
        /// 判断格子是否可作为导航目标使用。
        /// clearanceRadiusTiles > 0 时，要求周围若干格内都没有障碍物。
        /// </summary>
        public bool IsGoalNavigableTile(Coord gridCoord, int clearanceRadiusTiles = 0)
        {
            if (!IsOpenTile(gridCoord))
                return false;

            for (int dx = -clearanceRadiusTiles; dx <= clearanceRadiusTiles; dx++)
            {
                for (int dy = -clearanceRadiusTiles; dy <= clearanceRadiusTiles; dy++)
                {
                    Coord candidate = new Coord(gridCoord.x + dx, gridCoord.y + dy);
                    if (!IsOpenTile(candidate))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 从给定网格开始，搜索最近的可通行格子。
        /// </summary>
        public bool TryFindNearestOpenTile(
            Coord start,
            out Coord result,
            int maxSearchRadius = 8,
            int clearanceRadiusTiles = 0)
        {
            result = start;

            if (GeneratedObstacleMap == null || MapSize.x == 0 || MapSize.y == 0)
                return false;

            Coord clampedStart = new Coord(
                Mathf.Clamp(start.x, 0, MapSize.x - 1),
                Mathf.Clamp(start.y, 0, MapSize.y - 1)
            );

            if (IsGoalNavigableTile(clampedStart, clearanceRadiusTiles))
            {
                result = clampedStart;
                return true;
            }

            for (int radius = 1; radius <= maxSearchRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                            continue;

                        Coord candidate = new Coord(clampedStart.x + dx, clampedStart.y + dy);
                        if (IsGoalNavigableTile(candidate, clearanceRadiusTiles))
                        {
                            result = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        
        /// <summary>
        /// 将网格坐标转换为世界坐标
        /// </summary>
        public Vector3 GridToWorld(Coord gridCoord)
        {
            return CoordToPosition(gridCoord.x, gridCoord.y);
        }
        
        #endregion
    }
}
