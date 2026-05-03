using System.Collections.Generic;

public static class PlannerStatsStore
{
    public class Entry
    {
        public int    successCount;
        public int    failCount;
        public double totalTimeMs;
        public double totalPathM;
        public long   totalNodes;

        public int    TotalCount => successCount + failCount;
        public double AvgTimeMs  => successCount > 0 ? totalTimeMs / successCount : 0;
        public double AvgPathM   => successCount > 0 ? totalPathM  / successCount : 0;
        public long   AvgNodes   => successCount > 0 ? totalNodes  / successCount : 0;
    }

    public static readonly Dictionary<string, Entry> Data = new();
    public static PlannerRunRecord LastRecord;

    // 重置机器人时会向 Nav2 发静默 goal，这条结果不应污染实验数据
    public static bool SuppressNextRecord { get; set; }

    public static event System.Action OnUpdated;

    public static void Record(PlannerRunRecord r)
    {
        if (SuppressNextRecord)
        {
            SuppressNextRecord = false;
            return;
        }

        if (!Data.TryGetValue(r.Algorithm, out var e))
        {
            e = new Entry();
            Data[r.Algorithm] = e;
        }
        if (r.PathFound)
        {
            e.successCount++;
            e.totalTimeMs += r.PlanTimeMs;
            e.totalPathM  += r.PathLengthM;
            e.totalNodes  += r.NodesExpanded;
        }
        else
        {
            e.failCount++;
        }
        LastRecord = r;
        OnUpdated?.Invoke();
    }

    public static void Clear()
    {
        Data.Clear();
        LastRecord = null;
        OnUpdated?.Invoke();
    }
}

public class PlannerRunRecord
{
    public string Algorithm;
    public double PlanTimeMs;
    public double PathLengthM;
    public int    NodesExpanded;
    public bool   PathFound;
}
