using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Registry;

[assembly: InternalsVisibleTo("Odin.Services.Tests")]

namespace Odin.Services.LastSeen;

#nullable enable

public class LastSeenService(ISystemLevel2Cache<LastSeenService> cache, ILifetimeScope rootScope) : ILastSeenService
{
    private static readonly TimeSpan LastSeenEntryTtl = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan UpdateThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DatabaseLifeTime = TimeSpan.FromDays(365);

    // SEB:NOTE we need this local lists to batch updates to the database
    // since the cache can't provide us with the list. Stupid cache.
    private readonly ConcurrentDictionary<string, UnixTimeUtc> _lastSeenBySubject = new();

    //

    private static string BuildCacheKey(string subject) => "last-seen:" + subject;

    //

    public Task LastSeenNowAsync(OdinId odinId)
    {
        return PutLastSeenAsync(odinId, UnixTimeUtc.Now());
    }

    //

    public Task LastSeenNowAsync(string subject)
    {
        return PutLastSeenAsync(subject, UnixTimeUtc.Now());
    }

    //

    public Task PutLastSeenAsync(OdinId odinId, UnixTimeUtc lastSeen)
    {
        return PutLastSeenAsync(odinId.DomainName, lastSeen);
    }

    //

    public async Task PutLastSeenAsync(string subject, UnixTimeUtc lastSeen)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return;
        }

        if (_lastSeenBySubject.TryGetValue(subject, out var lastSeenCached))
        {
            var diff = TimeSpan.FromMilliseconds(lastSeen.milliseconds - lastSeenCached.milliseconds);
            if (diff < UpdateThreshold)
            {
                // Don't thrash the cache with updates that are too close to each other
                return;
            }
        }

        _lastSeenBySubject[subject] = lastSeen;

        await cache.SetAsync(BuildCacheKey(subject), lastSeen, LastSeenEntryTtl);
    }

    //

    public Task<UnixTimeUtc?> GetLastSeenAsync(OdinId odinId)
    {
        return GetLastSeenAsync(odinId.DomainName);
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(string subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }

        var result = await cache.GetOrSetAsync(
            BuildCacheKey(subject),
            async _ =>
            {
                await using var scope = rootScope.BeginLifetimeScope();
                var lastSeen = scope.Resolve<TableLastSeen>();
                return await lastSeen.GetLastSeenAsync(subject);
            },
            LastSeenEntryTtl);

        if (result == null)
        {
            return null;
        }

        _lastSeenBySubject[subject] = result.Value;
        return result;
    }

    //

    internal async Task UpdateDatabaseAsync()
    {
        if (_lastSeenBySubject.IsEmpty)
        {
            return;
        }

        var updates = _lastSeenBySubject.ToDictionary();
        _lastSeenBySubject.Clear();

        await using var scope = rootScope.BeginLifetimeScope();
        var lastSeen = scope.Resolve<TableLastSeen>();
        await lastSeen.UpdateLastSeenAsync(updates);
    }

    //

    internal async Task DeleteOldDatabaseRecords()
    {
        await using var scope = rootScope.BeginLifetimeScope();
        var lastSeen = scope.Resolve<TableLastSeen>();
        await lastSeen.DeleteOldRecordsAsync(DatabaseLifeTime);
    }

}
