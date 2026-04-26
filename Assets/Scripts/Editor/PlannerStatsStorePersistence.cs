using System.Globalization;
using System.Text;
using UnityEditor;

[InitializeOnLoad]
static class PlannerStatsStorePersistence
{
    internal const string SessionKey = "PlannerStats_v1";

    static PlannerStatsStorePersistence()
    {
        RestoreFromSession();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            SaveToSession();
    }

    internal static void SaveToSession()
    {
        var sb = new StringBuilder();
        foreach (var kv in PlannerStatsStore.Data)
        {
            var e = kv.Value;
            sb.AppendLine(string.Join("|",
                kv.Key,
                e.successCount,
                e.failCount,
                e.totalTimeMs.ToString("F3", CultureInfo.InvariantCulture),
                e.totalPathM.ToString("F3",  CultureInfo.InvariantCulture),
                e.totalNodes.ToString()));
        }
        SessionState.SetString(SessionKey, sb.ToString());
    }

    internal static void RestoreFromSession()
    {
        string raw = SessionState.GetString(SessionKey, "");
        if (string.IsNullOrEmpty(raw)) return;

        PlannerStatsStore.Data.Clear();
        foreach (string line in raw.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] p = line.Trim().Split('|');
            if (p.Length < 6) continue;

            PlannerStatsStore.Data[p[0]] = new PlannerStatsStore.Entry
            {
                successCount = int.Parse(p[1]),
                failCount    = int.Parse(p[2]),
                totalTimeMs  = double.Parse(p[3], CultureInfo.InvariantCulture),
                totalPathM   = double.Parse(p[4], CultureInfo.InvariantCulture),
                totalNodes   = long.Parse(p[5]),
            };
        }
    }

    internal static void ClearSession()
    {
        SessionState.EraseString(SessionKey);
    }
}
