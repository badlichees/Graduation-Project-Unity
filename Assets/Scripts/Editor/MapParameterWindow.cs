using RobotSimulation.MapGeneration;
using UnityEditor;
using UnityEngine;

public class MapParameterWindow : EditorWindow
{
    MapGenerator mapGenerator;
    bool autoRegenerate;
    int lastSeed;
    float lastObstaclePercent;
    int lastMapIndex;
    int lastGoalRadius;
    int lastRobotRadius;
    bool lastClearReservedAfterGeneration;
    bool lastKeepRobotInsideMap;
    bool lastDrawRobotNoObstacleZone;

    [MenuItem("Tools/地图参数面板 %#m")]
    static void Open() => GetWindow<MapParameterWindow>("地图参数面板");

    void OnGUI()
    {
        ResolveMapGenerator();
        DrawHeader();

        if (mapGenerator == null)
        {
            EditorGUILayout.HelpBox("场景中未找到 MapGenerator", MessageType.Warning);
            return;
        }

        var map = mapGenerator.ActiveMap;
        if (map == null)
        {
            EditorGUILayout.HelpBox("MapGenerator.maps 为空", MessageType.Warning);
            return;
        }

        Undo.RecordObject(mapGenerator, "Map Parameter Change");

        int previousIndex = mapGenerator.mapIndex;
        int previousSeed = map.seed;
        float previousObstaclePercent = map.obstaclePercent;
        int previousGoalRadius = mapGenerator.protectedGoalRadiusTiles;
        int previousRobotRadius = mapGenerator.protectedRobotRadiusTiles;
        bool previousClearReservedAfterGeneration = mapGenerator.clearReservedObstaclesAfterGeneration;
        bool previousKeepRobotInsideMap = mapGenerator.keepRobotInsideMapOnRegenerate;
        bool previousDrawRobotNoObstacleZone = mapGenerator.drawRobotNoObstacleZone;

        int newIndex = EditorGUILayout.IntSlider("mapIndex", mapGenerator.mapIndex, 0, mapGenerator.maps.Length - 1);
        if (newIndex != mapGenerator.mapIndex)
        {
            mapGenerator.mapIndex = newIndex;
            map = mapGenerator.ActiveMap;
            if (map == null) return;
        }

        int newSeed = EditorGUILayout.IntField("seed", map.seed);
        float newObstaclePercent = EditorGUILayout.Slider("obstaclePercent", map.obstaclePercent, 0f, 0.75f);
        mapGenerator.protectedGoalRadiusTiles = EditorGUILayout.IntSlider(
            "goal protected radius", mapGenerator.protectedGoalRadiusTiles, 0, 4);
        mapGenerator.protectedRobotRadiusTiles = EditorGUILayout.IntSlider(
            "robot protected radius", mapGenerator.protectedRobotRadiusTiles, 0, 6);
        mapGenerator.clearReservedObstaclesAfterGeneration = EditorGUILayout.Toggle(
            "clear reserved after generate", mapGenerator.clearReservedObstaclesAfterGeneration);
        mapGenerator.keepRobotInsideMapOnRegenerate = EditorGUILayout.Toggle(
            "keep robot inside map", mapGenerator.keepRobotInsideMapOnRegenerate);
        mapGenerator.drawRobotNoObstacleZone = EditorGUILayout.Toggle(
            "draw robot no-obstacle circle", mapGenerator.drawRobotNoObstacleZone);

        bool changed = previousIndex != newIndex ||
                       previousSeed != newSeed ||
                       !Mathf.Approximately(previousObstaclePercent, newObstaclePercent) ||
                       previousGoalRadius != mapGenerator.protectedGoalRadiusTiles ||
                       previousRobotRadius != mapGenerator.protectedRobotRadiusTiles ||
                       previousClearReservedAfterGeneration != mapGenerator.clearReservedObstaclesAfterGeneration ||
                       previousKeepRobotInsideMap != mapGenerator.keepRobotInsideMapOnRegenerate ||
                       previousDrawRobotNoObstacleZone != mapGenerator.drawRobotNoObstacleZone;

        mapGenerator.SetActiveMapParameters(newSeed, newObstaclePercent);

        EditorGUILayout.Space(6);
        autoRegenerate = EditorGUILayout.Toggle("变更后自动重新生成", autoRegenerate);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("随机种子", GUILayout.Height(26)))
            {
                mapGenerator.SetActiveMapParameters(Random.Range(0, int.MaxValue), mapGenerator.ActiveMap.obstaclePercent);
                Regenerate();
            }

            if (GUILayout.Button("重新生成地图", GUILayout.Height(26)))
                Regenerate();
        }

        if (changed)
        {
            EditorUtility.SetDirty(mapGenerator);
            if (autoRegenerate && HasStableParameterChange())
                Regenerate();
        }

        lastSeed = mapGenerator.ActiveMap.seed;
        lastObstaclePercent = mapGenerator.ActiveMap.obstaclePercent;
        lastMapIndex = mapGenerator.mapIndex;
        lastGoalRadius = mapGenerator.protectedGoalRadiusTiles;
        lastRobotRadius = mapGenerator.protectedRobotRadiusTiles;
        lastClearReservedAfterGeneration = mapGenerator.clearReservedObstaclesAfterGeneration;
        lastKeepRobotInsideMap = mapGenerator.keepRobotInsideMapOnRegenerate;
        lastDrawRobotNoObstacleZone = mapGenerator.drawRobotNoObstacleZone;
    }

    void DrawHeader()
    {
        EditorGUILayout.Space(4);
        GUILayout.Label("运行时地图参数", EditorStyles.boldLabel);
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.4f, 0.4f, 0.4f));
        EditorGUILayout.Space(4);
    }

    bool HasStableParameterChange()
    {
        var map = mapGenerator.ActiveMap;
        if (map == null) return false;
        return lastSeed != map.seed ||
               lastMapIndex != mapGenerator.mapIndex ||
               Mathf.Abs(lastObstaclePercent - map.obstaclePercent) > 0.001f ||
               lastGoalRadius != mapGenerator.protectedGoalRadiusTiles ||
               lastRobotRadius != mapGenerator.protectedRobotRadiusTiles ||
               lastClearReservedAfterGeneration != mapGenerator.clearReservedObstaclesAfterGeneration ||
               lastKeepRobotInsideMap != mapGenerator.keepRobotInsideMapOnRegenerate ||
               lastDrawRobotNoObstacleZone != mapGenerator.drawRobotNoObstacleZone;
    }

    void Regenerate()
    {
        mapGenerator.GenerateMap();
        EditorUtility.SetDirty(mapGenerator);
    }

    void ResolveMapGenerator()
    {
        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<MapGenerator>();
    }
}
