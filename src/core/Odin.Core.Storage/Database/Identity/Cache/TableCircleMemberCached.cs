using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

public class TableCircleMemberCached(TableCircleMember table, ITenantLevel2Cache cache) : AbstractTableCaching(cache)
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

    private async Task InvalidateAsync(Guid circleId, Guid memberId)
    {
        await RemoveAsync(GetCacheKey(circleId, memberId));
        await RemoveAsync(GetCircleCacheKey(circleId));
        await RemoveAsync(GetMemberCacheKey(memberId));
        await RemoveAsync(CacheKeyAll);
    }

    //

    public async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        var result = await table.DeleteAsync(circleId, memberId);
        await InvalidateAsync(circleId, memberId);
        return result;
    }

    //

    public async Task<int> InsertAsync(CircleMemberRecord item, TimeSpan ttl)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item.circleId, item.memberId);

        await SetAsync(
            GetCacheKey(item),
            item,
            ttl);

        return result;
    }

    //

    public async Task<int> UpsertAsync(CircleMemberRecord item, TimeSpan ttl)
    {
        var result = await table.UpsertAsync(item);

        await InvalidateAsync(item.circleId, item.memberId);

        await SetAsync(
            GetCacheKey(item),
            item,
            ttl);

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
        // SEB:NOTE We could iterate the list here and SET each item individually,
        // but that would probably be slower, so we just invalidate all.
        await InvalidateAllAsync();
    }

    //

    public async Task RemoveCircleMembersAsync(Guid circleId, List<Guid> members)
    {
        await table.RemoveCircleMembersAsync(circleId, members);
        // SEB:NOTE We could iterate the list here and REMOVE each item individually,
        // but that would probably be slower, so we just invalidate all.
        await InvalidateAllAsync();
    }

    //

    public async Task DeleteMembersFromAllCirclesAsync(List<Guid> members)
    {
        await table.DeleteMembersFromAllCirclesAsync(members);
        // SEB:NOTE We could iterate the list here and REMOVE each item individually,
        // but that would probably be slower, so we just invalidate all.
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