using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Odin.Core.Storage.Database;

#nullable enable

public interface ITransactionalCacheStats
{
    void ReportHit(string key);
    void ReportMiss(string key);
    (long Hits, long Misses) GetStats(string key);
    double GetHitRatio(string key);
    double GetOverallHitRatio();
    IReadOnlyDictionary<string, (long Hits, long Misses)> GetAllStats();
    void Clear();
}

//

public sealed class TransactionalCacheStats : ITransactionalCacheStats
{
    private sealed class Stats
    {
        public long HitCount;
        public long MissCount;
    }

    private readonly ConcurrentDictionary<string, Stats> _stats = new();

    //

    public void ReportHit(string key)
    {
        var stats = _stats.GetOrAdd(key, _ => new Stats());
        Interlocked.Increment(ref stats.HitCount);
    }

    //

    public void ReportMiss(string key)
    {
        var stats = _stats.GetOrAdd(key, _ => new Stats());
        Interlocked.Increment(ref stats.MissCount);
    }

    //

    public (long Hits, long Misses) GetStats(string key)
    {
        var stats = _stats.GetValueOrDefault(key, new Stats());
        return (stats.HitCount, stats.MissCount);
    }

    //

    public double GetHitRatio(string key)
    {
        var (hits, misses) = GetStats(key);
        var total = hits + misses;
        return total == 0 ? 0.0 : (double)hits / total;
    }

    //

    public double GetOverallHitRatio()
    {
        long totalHits = 0;
        long totalMisses = 0;

        foreach (var stats in _stats.Values)
        {
            totalHits += Interlocked.Read(ref stats.HitCount);
            totalMisses += Interlocked.Read(ref stats.MissCount);
        }

        var total = totalHits + totalMisses;
        return total == 0 ? 0.0 : (double)totalHits / total;
    }

    //

    public IReadOnlyDictionary<string, (long Hits, long Misses)> GetAllStats()
    {
        return _stats.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.HitCount, kvp.Value.MissCount));
    }

    //

    public void Clear()
    {
        _stats.Clear();
    }
}
