using System.Collections;
using UnityEngine;

/// <summary>
/// Play Mode 内一键重置机器人到初始位姿，同时清空路径线和目标标记。
/// 挂到场景中任意 GameObject（推荐与 GoalPublisher 同一对象）。
/// 快捷键默认 R，也可在 EditorWindow 中点按钮触发。
/// </summary>
public class RobotResetController : MonoBehaviour
{
    [Header("Robot")]
    [Tooltip("机器人根节点（含有根 ArticulationBody 的那个，通常是 base_footprint）")]
    public Transform robotRoot;

    [Header("Key Binding")]
    public KeyCode resetKey = KeyCode.R;

    [Header("References")]
    public PathVisualizer pathVisualizer;
    public GoalPublisher  goalPublisher;

    public static RobotResetController Instance { get; private set; }

    ArticulationBody rootBody;
    Vector3    initialPos;
    Quaternion initialRot;

    void Start()
    {
        // Play 启动时取消 Nav2 可能残留的旧 goal（应对上次 Play 未停 ROS 的情况）
        StartCoroutine(SendStopGoal());
    }

    void Awake()
    {
        Instance = this;

        if (robotRoot == null)
        {
            Debug.LogWarning("RobotResetController: 未指定 robotRoot，尝试自动查找...");
            var found = FindFirstObjectByType<ArticulationBody>();
            if (found != null) robotRoot = found.transform.root;
        }

        if (robotRoot != null)
        {
            // 找到 isRoot 的 ArticulationBody（URDF 导入后即根连杆）
            foreach (var ab in robotRoot.GetComponentsInChildren<ArticulationBody>())
            {
                if (ab.isRoot) { rootBody = ab; break; }
            }
        }

        if (rootBody != null)
        {
            initialPos = rootBody.transform.position;
            initialRot = rootBody.transform.rotation;
        }
        else
        {
            Debug.LogWarning("RobotResetController: 未找到根 ArticulationBody，重置功能不可用");
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(resetKey))
            ResetPose();
    }

    /// <summary>重置机器人到初始位姿，并强制停止 Nav2 导航。</summary>
    public void ResetPose()
    {
        if (rootBody == null)
        {
            Debug.LogWarning("RobotResetController: rootBody 为空，无法重置");
            return;
        }

        // 1. 传送机器人
        rootBody.TeleportRoot(initialPos, initialRot);

        // 2. 清视觉状态
        if (pathVisualizer != null) pathVisualizer.ClearPath();
        if (goalPublisher != null)  goalPublisher.ClearGoalMarker();

        // 3. 等 OdometryPublisher 的 FixedUpdate 发出新里程计后，
        //    再向 Nav2 发布"停在原地"的静默目标，强制取消当前 action goal
        StartCoroutine(SendStopGoal());

        Debug.Log($"RobotResetController: 机器人已重置至 {initialPos}");
    }

    IEnumerator SendStopGoal()
    {
        // 等一个物理帧，确保 OdometryPublisher 已将新位姿发给 ROS
        yield return new WaitForFixedUpdate();

        if (goalPublisher != null)
            goalPublisher.PublishSilentGoal(initialPos, initialRot);
    }
}
