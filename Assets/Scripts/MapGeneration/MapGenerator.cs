using UnityEngine;
using System;
using System.Collections;
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

        [Header("目标保护")]
        public global::GoalPublisher goalPublisher;
        [Min(0)] public int protectedGoalRadiusTiles = 1;

        [Header("Robot Protection")]
        public Transform robotTransform;
        [Min(0)] public int protectedRobotRadiusTiles = 2;
        public bool clearReservedObstaclesAfterGeneration = true;
        public bool keepRobotInsideMapOnRegenerate = true;
        [Min(0f)] public float robotSafeGroundY = 0.05f;
        public bool drawRobotNoObstacleZone = true;

        public event Action OnMapGenerated;

        #endregion
        
        #region 私有字段

        private List<Coord> allTileCoords;
        private Queue<Coord> shuffledTileCoords;
        private Map currentMap;
        private Dictionary<Coord, Transform> _obstacleObjects = new Dictionary<Coord, Transform>();
        private Coroutine robotGuardReleaseCoroutine;
        private ArticulationBody guardedRootBody;
        private bool robotGuardActive;

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
            ResolveSceneReferences();
            ArticulationBody guardedRobotBody = BeginRobotMapRegenerationGuard();
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
                Transform existingHolder = transform.Find(holderName);
                if (existingHolder != null)
                    DestroyGeneratedMapHolder(existingHolder);
                
                mapHolder = new GameObject(holderName).transform;
                mapHolder.parent = transform;
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
                
                if (!IsReservedCoord(randomCoord) &&
                    MapIsFullyAccessible(obstacleMap, currentObstacleCount))
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
            
            int clearedReservedObstacles = clearReservedObstaclesAfterGeneration
                ? ClearReservedObstacles(obstacleMap, allOpenCoords)
                : 0;
            currentObstacleCount -= clearedReservedObstacles;

            GeneratedObstacleMap = obstacleMap;
            if (keepRobotInsideMapOnRegenerate)
                EnsureRobotOnGeneratedMap(obstacleMap);
            if (Application.isPlaying)
                Physics.SyncTransforms();
            EndRobotMapRegenerationGuard(guardedRobotBody);
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
        
        public Coord GetRandomCoord()
        {
            Coord randomCoord = shuffledTileCoords.Dequeue();
            shuffledTileCoords.Enqueue(randomCoord);
            return randomCoord;
        }
        
        #endregion
        
        #region 数据导出
        
        // true 表示障碍物，供 ROS OccupancyGrid 读取
        public bool[,] GeneratedObstacleMap { get; private set; }

        public Coord MapSize
        {
            get { return currentMap != null ? currentMap.mapSize : new Coord(0, 0); }
        }

        // 点击、目标吸附通过同一套取整规则进入网格
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

        public Map ActiveMap
        {
            get
            {
                if (maps == null || maps.Length == 0) return null;
                int index = Mathf.Clamp(mapIndex, 0, maps.Length - 1);
                return maps[index];
            }
        }

        public void SetActiveMapParameters(int seed, float obstaclePercent)
        {
            Map map = ActiveMap;
            if (map == null) return;
            map.seed = seed;
            map.obstaclePercent = Mathf.Clamp01(obstaclePercent);
        }

        bool IsReservedCoord(Coord coord)
        {
            return coord == currentMap.mapCentre ||
                   IsProtectedGoalCoord(coord) ||
                   IsProtectedRobotCoord(coord);
        }

        int ClearReservedObstacles(bool[,] obstacleMap, List<Coord> allOpenCoords)
        {
            int cleared = 0;
            for (int x = 0; x < obstacleMap.GetLength(0); x++)
            {
                for (int y = 0; y < obstacleMap.GetLength(1); y++)
                {
                    if (!obstacleMap[x, y])
                        continue;

                    Coord coord = new Coord(x, y);
                    if (!IsReservedCoord(coord))
                        continue;

                    obstacleMap[x, y] = false;
                    if (!allOpenCoords.Contains(coord))
                        allOpenCoords.Add(coord);

                    if (_obstacleObjects.TryGetValue(coord, out Transform obstacle))
                    {
                        DestroyObstacleObject(obstacle);
                        _obstacleObjects.Remove(coord);
                    }
                    cleared++;
                }
            }

            return cleared;
        }

        void DestroyGeneratedMapHolder(Transform holder)
        {
            if (holder == null)
                return;

            if (Application.isPlaying)
            {
                foreach (Collider collider in holder.GetComponentsInChildren<Collider>())
                    collider.enabled = false;
                foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>())
                    renderer.enabled = false;
                Physics.SyncTransforms();
                Destroy(holder.gameObject);
            }
            else
            {
                DestroyImmediate(holder.gameObject);
            }
        }

        ArticulationBody BeginRobotMapRegenerationGuard()
        {
            ArticulationBody rootBody = FindRobotRootBody();
            if (rootBody == null || !Application.isPlaying)
                return rootBody;

            if (robotGuardReleaseCoroutine != null)
            {
                StopCoroutine(robotGuardReleaseCoroutine);
                robotGuardReleaseCoroutine = null;
            }

            if (!robotGuardActive || guardedRootBody != rootBody)
            {
                guardedRootBody = rootBody;
                robotGuardActive = true;
            }

            rootBody.immovable = true;
            StopRobotMotion(rootBody);
            return rootBody;
        }

        void EndRobotMapRegenerationGuard(ArticulationBody rootBody)
        {
            if (rootBody == null)
                return;

            StopRobotMotion(rootBody);

            if (Application.isPlaying)
            {
                if (robotGuardReleaseCoroutine != null)
                    StopCoroutine(robotGuardReleaseCoroutine);
                robotGuardReleaseCoroutine = StartCoroutine(ReleaseRobotGuardAfterPhysics(rootBody));
            }
            else
            {
                RestoreRobotGuard(rootBody);
            }
        }

        IEnumerator ReleaseRobotGuardAfterPhysics(ArticulationBody rootBody)
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            RestoreRobotGuard(rootBody);
        }

        void RestoreRobotGuard(ArticulationBody rootBody)
        {
            if (rootBody == null)
                return;

            StopRobotMotion(rootBody);
            rootBody.immovable = false;
            rootBody.WakeUp();

            robotGuardReleaseCoroutine = null;
            robotGuardActive = false;
            guardedRootBody = null;
        }

        static void StopRobotMotion(ArticulationBody rootBody)
        {
            if (rootBody == null)
                return;

            rootBody.velocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
            rootBody.Sleep();
        }

        void EnsureRobotOnGeneratedMap(bool[,] obstacleMap)
        {
            ResolveRobotTransform();
            if (robotTransform == null || obstacleMap == null)
                return;

            bool outsideMap = IsOutsideCurrentMap(robotTransform.position);
            Coord robotCoord = WorldToGrid(robotTransform.position);
            bool blockedCell = !IsInsideMap(robotCoord, obstacleMap) || obstacleMap[robotCoord.x, robotCoord.y];

            if (!outsideMap && !blockedCell)
                return;

            Coord targetCoord = FindNearestOpenCoord(robotCoord, obstacleMap);
            MoveRobotToMapCoord(targetCoord);
            Debug.Log($"MapGenerator: moved robot to safe map cell {targetCoord} after regeneration");
        }

        bool IsOutsideCurrentMap(Vector3 worldPosition)
        {
            if (currentMap == null)
                return false;

            float halfX = currentMap.mapSize.x * tileSize * 0.5f;
            float halfZ = currentMap.mapSize.y * tileSize * 0.5f;
            return worldPosition.x < -halfX || worldPosition.x > halfX ||
                   worldPosition.z < -halfZ || worldPosition.z > halfZ;
        }

        Coord FindNearestOpenCoord(Coord start, bool[,] obstacleMap)
        {
            Coord clampedStart = new Coord(
                Mathf.Clamp(start.x, 0, obstacleMap.GetLength(0) - 1),
                Mathf.Clamp(start.y, 0, obstacleMap.GetLength(1) - 1));

            if (!obstacleMap[clampedStart.x, clampedStart.y])
                return clampedStart;

            int maxRadius = Mathf.Max(obstacleMap.GetLength(0), obstacleMap.GetLength(1));
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                            continue;

                        Coord candidate = new Coord(clampedStart.x + dx, clampedStart.y + dy);
                        if (IsInsideMap(candidate, obstacleMap) && !obstacleMap[candidate.x, candidate.y])
                            return candidate;
                    }
                }
            }

            return currentMap.mapCentre;
        }

        void MoveRobotToMapCoord(Coord coord)
        {
            Vector3 targetPosition = GridToWorld(coord);
            targetPosition.y = Mathf.Max(robotTransform.position.y, robotSafeGroundY);

            ArticulationBody rootBody = FindRobotRootBody();
            if (rootBody != null)
            {
                Vector3 rootOffset = robotTransform.position - rootBody.transform.position;
                Vector3 rootTargetPosition = targetPosition - rootOffset;
                rootTargetPosition.y = Mathf.Max(rootTargetPosition.y, robotSafeGroundY);
                rootBody.TeleportRoot(rootTargetPosition, rootBody.transform.rotation);
            }
            else
            {
                robotTransform.position = targetPosition;
            }
        }

        ArticulationBody FindRobotRootBody()
        {
            if (robotTransform == null)
                return null;

            foreach (ArticulationBody body in robotTransform.GetComponentsInChildren<ArticulationBody>())
            {
                if (body.isRoot)
                    return body;
            }

            ArticulationBody parentBody = robotTransform.GetComponentInParent<ArticulationBody>();
            return parentBody != null && parentBody.isRoot ? parentBody : null;
        }

        void DestroyObstacleObject(Transform obstacle)
        {
            if (obstacle == null)
                return;

            if (Application.isPlaying)
                Destroy(obstacle.gameObject);
            else
                DestroyImmediate(obstacle.gameObject);
        }

        bool IsInsideMap(Coord coord, bool[,] obstacleMap)
        {
            return coord.x >= 0 && coord.x < obstacleMap.GetLength(0) &&
                   coord.y >= 0 && coord.y < obstacleMap.GetLength(1);
        }

        bool IsProtectedGoalCoord(Coord coord)
        {
            if (goalPublisher == null)
                goalPublisher = FindFirstObjectByType<global::GoalPublisher>();
            if (goalPublisher == null)
                return false;

            Vector3 goalPoint = goalPublisher.GoalProtectionPoint;
            Coord goalCoord = WorldToGrid(goalPoint);
            int radius = Mathf.Max(protectedGoalRadiusTiles, goalPublisher.GoalClearanceRadiusTiles);

            return Mathf.Abs(coord.x - goalCoord.x) <= radius &&
                   Mathf.Abs(coord.y - goalCoord.y) <= radius;
        }

        bool IsProtectedRobotCoord(Coord coord)
        {
            if (protectedRobotRadiusTiles <= 0)
                return false;

            ResolveRobotTransform();
            if (robotTransform == null)
                return false;

            Vector3 tileWorld = CoordToPosition(coord.x, coord.y);
            Vector2 robotXZ = new Vector2(robotTransform.position.x, robotTransform.position.z);
            Vector2 tileXZ = new Vector2(tileWorld.x, tileWorld.z);
            float noObstacleRadius = GetRobotNoObstacleRadiusWorld();
            float tileFootprintRadius = tileSize * 0.70710678f;
            float rejectRadius = noObstacleRadius + tileFootprintRadius;

            return (tileXZ - robotXZ).sqrMagnitude <= rejectRadius * rejectRadius;
        }

        float GetRobotNoObstacleRadiusWorld()
        {
            return Mathf.Max(0, protectedRobotRadiusTiles) * tileSize;
        }

        void ResolveRobotTransform()
        {
            Transform physicsTransform = ResolvePhysicsRobotTransform(robotTransform);
            if (physicsTransform != null)
            {
                robotTransform = physicsTransform;
                return;
            }

            ResolveSceneReferences();
        }

        void ResolveSceneReferences()
        {
            if (goalPublisher == null)
                goalPublisher = FindFirstObjectByType<global::GoalPublisher>();

            Transform physicsTransform = goalPublisher != null
                ? ResolvePhysicsRobotTransform(goalPublisher.robotTransform)
                : null;
            if (physicsTransform != null)
                robotTransform = physicsTransform;

            if (robotTransform != null)
                return;

            foreach (var body in FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None))
            {
                if (!body.isRoot)
                    continue;

                robotTransform = body.transform;
                return;
            }
        }

        Transform ResolvePhysicsRobotTransform(Transform candidate)
        {
            if (candidate == null)
                return null;

            ArticulationBody directBody = candidate.GetComponent<ArticulationBody>();
            if (directBody != null && directBody.isRoot)
                return directBody.transform;

            foreach (ArticulationBody body in candidate.GetComponentsInChildren<ArticulationBody>())
            {
                if (body.isRoot)
                    return body.transform;
            }

            ArticulationBody parentBody = candidate.GetComponentInParent<ArticulationBody>();
            if (parentBody != null && parentBody.isRoot)
                return parentBody.transform;

            return null;
        }

        void OnDrawGizmos()
        {
            if (!drawRobotNoObstacleZone || protectedRobotRadiusTiles <= 0)
                return;

            if (!Application.isPlaying && robotTransform == null)
                ResolveSceneReferences();

            if (robotTransform == null)
                return;

            DrawRobotNoObstacleCircle(robotTransform.position, GetRobotNoObstacleRadiusWorld());
        }

        void DrawRobotNoObstacleCircle(Vector3 centre, float radius)
        {
            const int segments = 64;
            if (radius <= 0f)
                return;

            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.85f);
            Vector3 previous = centre + new Vector3(radius, 0.05f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        #endregion
    }
}
