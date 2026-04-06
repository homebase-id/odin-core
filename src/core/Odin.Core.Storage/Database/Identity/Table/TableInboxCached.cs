using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

/// <summary>
/// Cached wrapper around TableInbox, following the same AbstractTableCaching pattern used by
/// TableDriveMainIndexCached, TableDrivesCached, etc.
///
/// WHY THIS EXISTS:
/// The inbox is a durable work queue for incoming peer transfers (files, read receipts, reactions,
/// deletes). Before this cache, every check for pending inbox items hit the database with a COUNT(*)
/// query — even when the inbox was empty, which is the common case. This matters because we plan to
/// auto-trigger inbox processing on every drive query (GetBatch, GetModified, etc.), meaning that
/// uncached count check would add a DB round-trip to every single drive read. With this cache, the
/// hot path (empty inbox) is a pure in-memory lookup returning 0 — zero DB calls added. The cache
/// is invalidated on every mutation (insert, pop, commit, cancel, recover) so it never serves stale
/// data that would cause items to be missed.
/// </summary>
public class TableInboxCached(TableInbox table, IIdentityTransactionalCacheFactory cacheFactory)
    : AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    //

    private static string GetCacheKey(Guid boxId)
    {
        return "readycount:" + boxId;
    }

    //

    private async Task InvalidateBoxAsync(Guid boxId)
    {
        await Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(boxId))
        ]);
    }

    //

    public async Task<int> GetReadyCountAsync(Guid boxId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(boxId),
            _ => table.GetReadyCountAsync(boxId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> InsertAsync(InboxRecord item)
    {
        var result = await table.InsertAsync(item);
        await InvalidateBoxAsync(item.boxId);
        return result;
    }

    //

    public async Task<int> UpsertAsync(InboxRecord item)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateBoxAsync(item.boxId);
        return result;
    }

    //

    public async Task<List<InboxRecord>> PopSpecificBoxAsync(Guid boxId, int count)
    {
        var result = await table.PopSpecificBoxAsync(boxId, count);
        await InvalidateBoxAsync(boxId);
        return result;
    }

    //

    public async Task<(int totalCount, int poppedCount, UnixTimeUtc oldestItemTime)> PopStatusSpecificBoxAsync(Guid boxId)
    {
        return await table.PopStatusSpecificBoxAsync(boxId);
    }

    //

    public async Task<(int, int, UnixTimeUtc)> PopStatusAsync()
    {
        return await table.PopStatusAsync();
    }

    //

    public async Task<int> PopCancelAllAsync(Guid popstamp)
    {
        var result = await table.PopCancelAllAsync(popstamp);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> PopCancelListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
    {
        var result = await table.PopCancelListAsync(popstamp, driveId, listFileId);
        await InvalidateBoxAsync(driveId);
        return result;
    }

    //

    public async Task<int> PopCommitAllAsync(Guid popstamp)
    {
        var result = await table.PopCommitAllAsync(popstamp);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //

    public async Task<int> PopCommitListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
    {
        var result = await table.PopCommitListAsync(popstamp, driveId, listFileId);
        await InvalidateBoxAsync(driveId);
        return result;
    }

    //

    public async Task<int> PopRecoverDeadAsync(UnixTimeUtc time)
    {
        var result = await table.PopRecoverDeadAsync(time);
        await Cache.InvalidateAllAsync();
        return result;
    }

    //
}
