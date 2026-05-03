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

    // 地图数据、场景实例和 ROS 发布都从这里派生，避免三份状态互相漂移
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

        public event Action OnMapGenerated;

        #endregion
        
        #region 私有字段

        private List<Coord> allTileCoords;
        private Queue<Coord> shuffledTileCoords;
        private Queue<Coord> shuffledOpenTileCoords;
        private Transform[,] tileMap;
        private Map currentMap;
        private Transform _mapHolder;
        private Dictionary<Coord, Transform> _obstacleObjects = new Dictionary<Coord, Transform>();

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
            _obstacleObjects.Clear();
            System.Random prng = new System.Random(currentMap.seed);
            
            allTileCoords = new List<Coord>();
            for (int x = 0; x < currentMap.mapSize.x; x++)
            {
                for (int y = 0; y < currentMap.mapSize.y; y++)
                {
                    allTileCoords.Add(new Coord(x, y));
                }
            }
            shuffledTileCoords = new Queue<Coord>(Utility.ShuffleArray(allTileCoords.ToArray(), currentMap.seed));
            
            // 每次重新生成都替换容器，避免旧障碍物残留到 ROS map
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
                _mapHolder = mapHolder;
            }
            
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
                        _obstacleObjects[randomCoord] = newObstacle;
                        
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

            GeneratedObstacleMap = obstacleMap;
            Debug.Log($"地图生成完成，尺寸：{currentMap.mapSize.x}x{currentMap.mapSize.y}，障碍物数量：{currentObstacleCount}");
            OnMapGenerated?.Invoke();
        }
        
        #endregion
        
        #region 地图验证和实用方法
        
        // 只接受全连通地图，避免 Nav2 目标吸附到逻辑上不可达的孤岛
        private bool MapIsFullyAccessible(bool[,] obstacleMap, int currentObstacleCount)
        {
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
        
        // true 表示障碍物，供 ROS OccupancyGrid 和动态障碍编辑共用
        public bool[,] GeneratedObstacleMap { get; private set; }

        public Coord MapSize
        {
            get { return currentMap != null ? currentMap.mapSize : new Coord(0, 0); }
        }
        
        public Vector3 MapCentreWorld
        {
            get { return CoordToPosition(MapSize.x / 2, MapSize.y / 2); }
        }
        
        // 点击、目标吸附和动态障碍都通过同一套取整规则进入网格
        public Coord WorldToGrid(Vector3 worldPos)
        {
            int x = Mathf.RoundToInt(worldPos.x / tileSize + (MapSize.x - 1) / 2f);
            int y = Mathf.RoundToInt(worldPos.z / tileSize + (MapSize.y - 1) / 2f);
            x = Mathf.Clamp(x, 0, MapSize.x - 1);
            y = Mathf.Clamp(y, 0, MapSize.y - 1);
            return new Coord(x, y);
        }

        public bool IsOpenTile(Coord gridCoord)
        {
            if (GeneratedObstacleMap == null)
                return false;

            if (gridCoord.x < 0 || gridCoord.x >= MapSize.x || gridCoord.y < 0 || gridCoord.y >= MapSize.y)
                return false;

            return !GeneratedObstacleMap[gridCoord.x, gridCoord.y];
        }

        public bool IsGoalNavigableTile(Coord gridCoord, int clearanceRadiusTiles = 0)
        {
            if (!IsOpenTile(gridCoord))
                return false;

            // 目标点需要预留车体净空，不能只检查中心格
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

            // 按方环向外找，优先保留用户点击点附近的导航意图
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

        public Vector3 GridToWorld(Coord gridCoord)
        {
            return CoordToPosition(gridCoord.x, gridCoord.y);
        }

        public Transform MapHolder => _mapHolder;

        public bool TryGetObstacleAt(Transform t, out Coord coord)
        {
            foreach (var kv in _obstacleObjects)
            {
                if (kv.Value == t) { coord = kv.Key; return true; }
            }
            coord = default;
            return false;
        }

        public void SetObstacleAtGrid(Coord coord, bool isObstacle)
        {
            if (GeneratedObstacleMap == null || currentMap == null) return;
            if (coord.x < 0 || coord.x >= MapSize.x || coord.y < 0 || coord.y >= MapSize.y) return;

            if (isObstacle)
            {
                if (GeneratedObstacleMap[coord.x, coord.y]) return;
                if (obstaclePrefab == null || _mapHolder == null) return;

                // 动态障碍使用固定中间高度，避免运行时编辑引入额外随机性
                float height = (currentMap.minObstacleHeight + currentMap.maxObstacleHeight) * 0.5f;
                Vector3 pos = CoordToPosition(coord.x, coord.y) + Vector3.up * height * 0.5f;
                Transform newObs = Instantiate(obstaclePrefab, pos, Quaternion.identity);
                newObs.parent = _mapHolder;
                newObs.localScale = new Vector3((1 - outlinePercent) * tileSize, height, (1 - outlinePercent) * tileSize);

                Renderer rend = newObs.GetComponent<Renderer>();
                if (rend != null)
                {
                    Material mat = new Material(rend.sharedMaterial);
                    float colourPercent = coord.y / (float)MapSize.y;
                    mat.color = Color.Lerp(currentMap.foregroundColour, currentMap.backgroundColour, colourPercent);
                    rend.sharedMaterial = mat;
                }

                _obstacleObjects[coord] = newObs;
                GeneratedObstacleMap[coord.x, coord.y] = true;
            }
            else
            {
                if (!GeneratedObstacleMap[coord.x, coord.y]) return;

                if (_obstacleObjects.TryGetValue(coord, out Transform t))
                {
                    Destroy(t.gameObject);
                    _obstacleObjects.Remove(coord);
                }
                GeneratedObstacleMap[coord.x, coord.y] = false;
            }

            OnMapGenerated?.Invoke();
        }

        #endregion
    }
}
