using System.Collections.Concurrent;
using System.Collections.Generic;
using RosMessageTypes.ActionMsgs;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public class NavigationRunMonitor : MonoBehaviour
{
    [Header("References")]
    public GoalPublisher goalPublisher;
    public Transform robotTransform;

    [Header("Completion")]
    [Min(0.02f)] public float goalTolerance = 0.18f;
    [Min(1f)] public float maxRunSeconds = 180f;

    [Header("Nav2 Status")]
    public bool completeOnNav2Succeeded = true;
    public string nav2StatusTopic = "/navigate_to_pose/_action/status";
    [Min(0f)] public float nav2ClockSkewToleranceSeconds = 2f;
    [Min(0f)] public float statsFlushDelayAfterSuccess = 0.2f;

    readonly ConcurrentQueue<GoalStatusArrayMsg> pendingStatusMessages = new();
    readonly HashSet<string> seenSucceededGoalIds = new();
    int succeededGoalToken;
    int observedRunSequence;
    int succeededGoalTokenAtRunStart;
    float nav2SucceededObservedAt = -1f;

    void Start()
    {
        if (!completeOnNav2Succeeded || string.IsNullOrEmpty(nav2StatusTopic))
            return;

        ROSConnection.GetOrCreateInstance().Subscribe<GoalStatusArrayMsg>(nav2StatusTopic, OnNav2Status);
        Debug.Log("NavigationRunMonitor: subscribed Nav2 status " + nav2StatusTopic);
    }

void Update()
    {
        ResolveReferences();
        TrackActiveRunStart();
        PlannerStatsReceiver.FlushPendingRecords();
        ProcessNav2StatusMessages();

        if (!PlannerStatsStore.HasActiveRun || robotTransform == null)
        {
            nav2SucceededObservedAt = -1f;
            return;
        }

        PlannerStatsStore.UpdateRobotPosition(robotTransform.position);

        if (completeOnNav2Succeeded && succeededGoalToken > succeededGoalTokenAtRunStart)
        {
            if (!StatsReadyAfterNav2Succeeded())
                return;

            CompleteCurrentRun("Nav2 status SUCCEEDED");
            return;
        }

        Vector3 goal = PlannerStatsStore.ActiveGoalUnity;
        Vector2 robotXZ = new Vector2(robotTransform.position.x, robotTransform.position.z);
        Vector2 goalXZ = new Vector2(goal.x, goal.z);

        if (Vector2.Distance(robotXZ, goalXZ) <= goalTolerance)
        {
            CompleteCurrentRun("Unity distance tolerance");
            return;
        }

        if (PlannerStatsStore.ActiveRunSeconds > maxRunSeconds)
        {
            PlannerStatsStore.CancelActiveRun();
            Debug.LogWarning("NavigationRunMonitor: active run timed out and was cancelled");
        }
    }

    void OnNav2Status(GoalStatusArrayMsg msg)
    {
        pendingStatusMessages.Enqueue(msg);
    }

    void TrackActiveRunStart()
    {
        int sequence = PlannerStatsStore.ActiveRunSequence;
        if (sequence == observedRunSequence)
            return;

        observedRunSequence = sequence;
        succeededGoalTokenAtRunStart = succeededGoalToken;
        nav2SucceededObservedAt = -1f;
    }

    void ProcessNav2StatusMessages()
    {
        while (pendingStatusMessages.TryDequeue(out GoalStatusArrayMsg msg))
        {
            if (msg?.status_list == null)
                continue;

            foreach (GoalStatusMsg status in msg.status_list)
            {
                if (status == null || status.status != GoalStatusMsg.STATUS_SUCCEEDED)
                    continue;

                string goalId = GetGoalId(status);
                if (!seenSucceededGoalIds.Add(goalId))
                    continue;

                if (PlannerStatsStore.HasActiveRun && IsStatusForCurrentRun(status))
                    succeededGoalToken++;
            }
        }
    }

    bool IsStatusForCurrentRun(GoalStatusMsg status)
    {
        double statusUnixSeconds = GetUnixSeconds(status.goal_info?.stamp);
        double runStartUnixSeconds = PlannerStatsStore.ActiveRunStartUnixSeconds;

        if (statusUnixSeconds < 1000000000.0 || runStartUnixSeconds < 1000000000.0)
            return true;

        return statusUnixSeconds + nav2ClockSkewToleranceSeconds >= runStartUnixSeconds;
    }

    static double GetUnixSeconds(RosMessageTypes.BuiltinInterfaces.TimeMsg stamp)
    {
        if (stamp == null)
            return 0.0;

        return stamp.sec + stamp.nanosec / 1000000000.0;
    }

    static string GetGoalId(GoalStatusMsg status)
    {
        byte[] uuid = status.goal_info?.goal_id?.uuid;
        if (uuid == null || uuid.Length == 0)
            return "empty-" + Time.frameCount;

        return System.BitConverter.ToString(uuid);
    }

    void CompleteCurrentRun(string source)
    {
        PlannerStatsReceiver.FlushPendingRecords();
        PlannerStatsStore.CompleteActiveRun();
        nav2SucceededObservedAt = -1f;
        Debug.Log("NavigationRunMonitor: goal succeeded via " + source + ", metrics recorded");
    }

    bool StatsReadyAfterNav2Succeeded()
    {
        if (PlannerStatsStore.HasPendingRecord)
            return true;

        if (nav2SucceededObservedAt < 0f)
            nav2SucceededObservedAt = Time.realtimeSinceStartup;

        PlannerStatsReceiver.FlushPendingRecords();
        return PlannerStatsStore.HasPendingRecord ||
               Time.realtimeSinceStartup - nav2SucceededObservedAt >= statsFlushDelayAfterSuccess;
    }

    void ResolveReferences()
    {
        if (goalPublisher == null)
            goalPublisher = FindFirstObjectByType<GoalPublisher>();

        if (robotTransform == null && goalPublisher != null)
            robotTransform = goalPublisher.robotTransform;
    }
}
