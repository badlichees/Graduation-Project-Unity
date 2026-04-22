using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GoalPublisher))]
public class GoalPublisherEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var publisher = (GoalPublisher)target;

        if (publisher.availableAlgorithms == null || publisher.availableAlgorithms.Length == 0)
            return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Algorithm Switcher", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < publisher.availableAlgorithms.Length; i++)
        {
            bool isActive = i == publisher.selectedAlgorithmIndex;
            GUI.color = isActive ? new Color(0.4f, 0.9f, 0.4f) : Color.white;

            if (GUILayout.Button(publisher.availableAlgorithms[i], GUILayout.Height(28)))
            {
                if (Application.isPlaying)
                    publisher.SelectAlgorithm(i);
                else
                    publisher.selectedAlgorithmIndex = i;
            }
        }
        GUI.color = Color.white;
        EditorGUILayout.EndHorizontal();

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox($"当前规划算法: {publisher.ActiveAlgorithm}", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("进入 Play 模式后点击按钮可实时切换算法，按 Tab 键亦可循环切换。", MessageType.None);
        }
    }
}
