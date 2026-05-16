using System.Collections;
using UnityEngine;

// 重置后主动给 Nav2 一个初始位姿 goal，避免旧 goal 继续驱动车体
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
            // URDF 导入层级较深，真正可 Teleport 的通常是 isRoot 关节
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

public void ResetPose()
    {
        if (rootBody == null)
        {
            Debug.LogWarning("RobotResetController: rootBody 为空，无法重置");
            return;
        }

        if (PlannerStatsStore.HasActiveRun)
            PlannerStatsStore.RecordExperimentFailure(PlannerStatsStore.ActiveAlgorithm);

        rootBody.TeleportRoot(initialPos, initialRot);

        if (pathVisualizer != null) pathVisualizer.ClearPath();
        if (goalPublisher != null)  goalPublisher.ClearGoalMarker();

        StartCoroutine(SendStopGoal());

        Debug.Log($"RobotResetController: 机器人已重置至 {initialPos}");
    }

    IEnumerator SendStopGoal()
    {
        // 等一个物理帧，让 odom 先发布重置后的位姿
        yield return new WaitForFixedUpdate();

        if (goalPublisher != null)
            goalPublisher.PublishSilentGoal(initialPos, initialRot);
    }
}
