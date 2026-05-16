using System.Globalization;
using System.Text;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using UnityEngine;

public class PlannerParameterPublisher : MonoBehaviour
{
    [Header("ROS")]
    public string topicName = "/planner_param_updates";
    public bool autoPublishOnStart = true;

    [Header("Weighted A*")]
    [Range(1f, 8f)] public float weightedAStarWeight = 2.5f;

    [Header("RRT*")]
    [Min(100)] public int rrtStarMaxIterations = 5000;
    [Min(0.05f)] public float rrtStarStepSize = 0.5f;
    [Min(0.1f)] public float rrtStarSearchRadius = 1.5f;
    [Min(0.05f)] public float rrtStarGoalTolerance = 0.5f;
    [Range(0f, 1f)] public float rrtStarGoalBias = 0.1f;

    [Header("DWB Local Planner")]
    [Min(0.01f)] public float maxVelX = 0.22f;
    [Min(0.1f)] public float maxVelTheta = 1.0f;
    [Min(0.1f)] public float simTime = 1.0f;
    [Range(5, 80)] public int vxSamples = 20;
    [Range(5, 120)] public int vthetaSamples = 30;

    ROSConnection ros;
    bool ready;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(topicName, 10);
        ready = true;

        if (autoPublishOnStart)
            PublishCurrentParameters();
    }

    public void PublishCurrentParameters()
    {
        if (!EnsureReady()) return;

        Publish("planner_server", BuildPlannerParametersJson());
        Publish("controller_server", BuildControllerParametersJson());
    }

    bool EnsureReady()
    {
        if (ready) return true;
        if (!Application.isPlaying) return false;

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(topicName, 10);
        ready = true;
        return true;
    }

    void Publish(string nodeName, string parameterJson)
    {
        string json = $"{{\"node\":\"{nodeName}\",\"parameters\":{parameterJson}}}";
        ros.Publish(topicName, new StringMsg(json));
        Debug.Log($"PlannerParameterPublisher: 已发布 {nodeName} 参数 {parameterJson}");
    }

    string BuildPlannerParametersJson()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        Append(sb, "WAStar.weight", weightedAStarWeight); sb.Append(',');
        Append(sb, "RRTStar.max_iterations", rrtStarMaxIterations); sb.Append(',');
        Append(sb, "RRTStar.step_size", rrtStarStepSize); sb.Append(',');
        Append(sb, "RRTStar.search_radius", rrtStarSearchRadius); sb.Append(',');
        Append(sb, "RRTStar.goal_tolerance", rrtStarGoalTolerance); sb.Append(',');
        Append(sb, "RRTStar.goal_bias", rrtStarGoalBias);
        sb.Append('}');
        return sb.ToString();
    }

    string BuildControllerParametersJson()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        Append(sb, "FollowPath.max_vel_x", maxVelX); sb.Append(',');
        Append(sb, "FollowPath.max_speed_xy", maxVelX); sb.Append(',');
        Append(sb, "FollowPath.max_vel_theta", maxVelTheta); sb.Append(',');
        Append(sb, "FollowPath.sim_time", simTime); sb.Append(',');
        Append(sb, "FollowPath.vx_samples", vxSamples); sb.Append(',');
        Append(sb, "FollowPath.vtheta_samples", vthetaSamples);
        sb.Append('}');
        return sb.ToString();
    }

    static void Append(StringBuilder sb, string key, float value)
    {
        sb.Append('"').Append(key).Append("\":")
          .Append(value.ToString("0.0###", CultureInfo.InvariantCulture));
    }

    static void Append(StringBuilder sb, string key, int value)
    {
        sb.Append('"').Append(key).Append("\":").Append(value);
    }
}
