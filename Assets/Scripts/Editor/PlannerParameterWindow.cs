using UnityEditor;
using UnityEngine;

public class PlannerParameterWindow : EditorWindow
{
    PlannerParameterPublisher publisher;
    Vector2 scroll;

    [MenuItem("Tools/规划参数面板 %#o")]
    static void Open() => GetWindow<PlannerParameterWindow>("规划参数面板");

    void OnEnable()
    {
        ResolvePublisher();
    }

    void OnGUI()
    {
        ResolvePublisher();
        DrawHeader();

        if (publisher == null)
        {
            EditorGUILayout.HelpBox("场景中未找到 PlannerParameterPublisher", MessageType.Warning);
            if (GUILayout.Button("创建参数发布器", GUILayout.Height(28)))
                CreatePublisher();
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        Undo.RecordObject(publisher, "Planner Parameter Change");

        DrawWeightedAStar();
        DrawRrtStar();
        DrawDwb();

        if (GUI.changed)
            EditorUtility.SetDirty(publisher);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(6);

        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button("发布参数到 ROS2", GUILayout.Height(30)))
                publisher.PublishCurrentParameters();
        }

        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("进入 Play Mode 后会通过 /planner_param_updates 动态应用参数", MessageType.None);
    }

    void DrawHeader()
    {
        EditorGUILayout.Space(4);
        GUILayout.Label("Nav2 规划参数", EditorStyles.boldLabel);
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.4f, 0.4f, 0.4f));
        EditorGUILayout.Space(4);
    }

    void DrawWeightedAStar()
    {
        GUILayout.Label("Weighted A*", EditorStyles.boldLabel);
        publisher.weightedAStarWeight = EditorGUILayout.Slider("weight", publisher.weightedAStarWeight, 1f, 8f);
        EditorGUILayout.Space(6);
    }

    void DrawRrtStar()
    {
        GUILayout.Label("RRT*", EditorStyles.boldLabel);
        publisher.rrtStarMaxIterations = EditorGUILayout.IntSlider("max_iterations", publisher.rrtStarMaxIterations, 100, 20000);
        publisher.rrtStarStepSize = EditorGUILayout.Slider("step_size", publisher.rrtStarStepSize, 0.05f, 2.0f);
        publisher.rrtStarSearchRadius = EditorGUILayout.Slider("search_radius", publisher.rrtStarSearchRadius, 0.1f, 5.0f);
        publisher.rrtStarGoalTolerance = EditorGUILayout.Slider("goal_tolerance", publisher.rrtStarGoalTolerance, 0.05f, 2.0f);
        publisher.rrtStarGoalBias = EditorGUILayout.Slider("goal_bias", publisher.rrtStarGoalBias, 0f, 1f);
        EditorGUILayout.Space(6);
    }

    void DrawDwb()
    {
        GUILayout.Label("DWB Local Planner", EditorStyles.boldLabel);
        publisher.maxVelX = EditorGUILayout.Slider("max_vel_x", publisher.maxVelX, 0.01f, 0.8f);
        publisher.maxVelTheta = EditorGUILayout.Slider("max_vel_theta", publisher.maxVelTheta, 0.1f, 3.0f);
        publisher.simTime = EditorGUILayout.Slider("sim_time", publisher.simTime, 0.2f, 4.0f);
        publisher.vxSamples = EditorGUILayout.IntSlider("vx_samples", publisher.vxSamples, 5, 80);
        publisher.vthetaSamples = EditorGUILayout.IntSlider("vtheta_samples", publisher.vthetaSamples, 5, 120);
    }

    void ResolvePublisher()
    {
        if (publisher == null)
            publisher = FindFirstObjectByType<PlannerParameterPublisher>();
    }

    void CreatePublisher()
    {
        var go = new GameObject("PlannerParameterPublisher");
        publisher = go.AddComponent<PlannerParameterPublisher>();
        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
    }
}
