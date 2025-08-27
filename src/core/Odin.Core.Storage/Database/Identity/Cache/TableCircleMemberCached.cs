using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableCircleMemberCached(
    TableCircleMember table,
    ITenantLevel2Cache cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : AbstractTableCaching(cache, scopedConnectionFactory)
{
    private const string CacheKeyAll = "all:all:all";

    //

    private static string GetCacheKey(CircleMemberRecord item)
    {
        return GetCacheKey(item.circleId, item.memberId);
    }

    //

    private static string GetCacheKey(Guid circleId, Guid memberId)
    {
        return circleId + ":" + memberId;
    }

    //

    private static string GetCircleCacheKey(Guid circleId)
    {
        return "circle:" + circleId;
    }

    //

    private static string GetMemberCacheKey(Guid memberId)
    {
        return "member:" + memberId;
    }

    //

    private Task InvalidateAsync(CircleMemberRecord item)
    {
        return InvalidateAsync(item.circleId, item.memberId);
    }

    //

    private async Task InvalidateAsync(Guid circleId, Guid memberId)
    {
        await InvalidateAsync([
            CreateRemoveByKeyAction(GetCacheKey(circleId, memberId)),
            CreateRemoveByKeyAction(GetCircleCacheKey(circleId)),
            CreateRemoveByKeyAction(GetMemberCacheKey(memberId)),
            CreateRemoveByKeyAction(CacheKeyAll)
        ]);
    }

    //

    public async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        var result = await table.DeleteAsync(circleId, memberId);

        await InvalidateAsync(circleId, memberId);

        return result;
    }

    //

    public async Task<int> InsertAsync(CircleMemberRecord item)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<int> UpsertAsync(CircleMemberRecord item)
    {
        var result = await table.UpsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetCircleCacheKey(circleId),
            _ => table.GetCircleMembersAsync(circleId),
            ttl);
        return result;
    }

    //

    public async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid memberId, TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            GetMemberCacheKey(memberId),
            _ => table.GetMemberCirclesAndDataAsync(memberId),
            ttl);
        return result;
    }

    //

    public async Task UpsertCircleMembersAsync(List<CircleMemberRecord> circleMemberRecordList)
    {
        await table.UpsertCircleMembersAsync(circleMemberRecordList);
        // SEB:NOTE We could iterate the list here and invalidate each item individually,
        // but that would probably be slow, so for now we just invalidate all.
        await InvalidateAllAsync();
    }

    //

    public async Task RemoveCircleMembersAsync(Guid circleId, List<Guid> members)
    {
        await table.RemoveCircleMembersAsync(circleId, members);
        // SEB:NOTE We could iterate the list here and invalidate each item individually,
        // but that would probably be slow, so for now we just invalidate all.
        await InvalidateAllAsync();
    }

    //

    public async Task DeleteMembersFromAllCirclesAsync(List<Guid> members)
    {
        await table.DeleteMembersFromAllCirclesAsync(members);
        // SEB:NOTE We could iterate the list here and invalidate each item individually,
        // but that would probably be slow, so for now we just invalidate all.
        await InvalidateAllAsync();
    }

    //

    public async Task<List<CircleMemberRecord>> GetAllCirclesAsync(TimeSpan ttl)
    {
        var result = await GetOrSetAsync(
            CacheKeyAll,
            _ => table.GetAllCirclesAsync(),
            ttl);
        return result;
    }

    //

}