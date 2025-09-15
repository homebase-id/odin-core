using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableCircleMemberCached(TableCircleMember table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    private const string CacheKeyAll = "all:all:all";

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
        await Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(circleId, memberId)),
            Cache.CreateRemoveByKeyAction(GetCircleCacheKey(circleId)),
            Cache.CreateRemoveByKeyAction(GetMemberCacheKey(memberId)),
            Cache.CreateRemoveByKeyAction(CacheKeyAll)
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
        var result = await Cache.GetOrSetAsync(
            GetCircleCacheKey(circleId),
            _ => table.GetCircleMembersAsync(circleId),
            ttl);
        return result;
    }

    //

    public async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid memberId, TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
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
        await Cache.InvalidateAllAsync();
    }

    //

    public async Task RemoveCircleMembersAsync(Guid circleId, List<Guid> members)
    {
        await table.RemoveCircleMembersAsync(circleId, members);
        // SEB:NOTE We could iterate the list here and invalidate each item individually,
        // but that would probably be slow, so for now we just invalidate all.
        await Cache.InvalidateAllAsync();
    }

    //

    public async Task DeleteMembersFromAllCirclesAsync(List<Guid> members)
    {
        await table.DeleteMembersFromAllCirclesAsync(members);
        // SEB:NOTE We could iterate the list here and invalidate each item individually,
        // but that would probably be slow, so for now we just invalidate all.
        await Cache.InvalidateAllAsync();
    }

    //

    public async Task<List<CircleMemberRecord>> GetAllCirclesAsync(TimeSpan ttl)
    {
        var result = await Cache.GetOrSetAsync(
            CacheKeyAll,
            _ => table.GetAllCirclesAsync(),
            ttl);
        return result;
    }

    //

}