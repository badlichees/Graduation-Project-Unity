using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSimulation.MapGeneration
{
    /// <summary>
    /// 地图生成器测试脚本，仅在Editor模式下使用
    /// </summary>
    public class TestMapGenerator : MonoBehaviour
    {
        public MapGenerator mapGenerator;
        public bool generateOnStart = false;
        
        private void Start()
        {
            if (generateOnStart && mapGenerator != null)
            {
                GenerateAndLog();
            }
        }
        
        public void GenerateAndLog()
        {
            if (mapGenerator == null)
            {
                Debug.LogError("MapGenerator未赋值！");
                return;
            }
            
            mapGenerator.GenerateMap();
            
            // 输出生成的地图信息
            var obstacleMap = mapGenerator.GeneratedObstacleMap;
            if (obstacleMap != null)
            {
                int width = obstacleMap.GetLength(0);
                int height = obstacleMap.GetLength(1);
                int obstacleCount = 0;
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        if (obstacleMap[x, y]) obstacleCount++;
                    }
                }
                Debug.Log($"地图生成测试完成，尺寸：{width}x{height}，障碍物数量：{obstacleCount}");
                
                // 创建GridMap并测试坐标转换
                GridMap gridMap = new GridMap(obstacleMap, mapGenerator.tileSize, mapGenerator.MapCentreWorld);
                Debug.Log($"GridMap创建成功，分辨率：{gridMap.resolution}，原点：{gridMap.origin}");
                
                // 测试中心点转换
                Coord centreGrid = mapGenerator.WorldToGrid(mapGenerator.MapCentreWorld);
                Vector3 centreWorld = mapGenerator.GridToWorld(centreGrid);
                Debug.Log($"中心点转换测试：世界坐标 {mapGenerator.MapCentreWorld} -> 网格坐标 {centreGrid} -> 世界坐标 {centreWorld}");
            }
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(TestMapGenerator))]
    public class TestMapGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            TestMapGenerator test = target as TestMapGenerator;
            if (GUILayout.Button("生成地图并测试"))
            {
                test.GenerateAndLog();
            }
        }
    }
#endif
}