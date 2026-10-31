using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Stores a member's sealed copy of the circle key (<see cref="TableCircleMember.SetSealedShareKeyAsync"/>).</summary>
    public async Task<int> SetSealedShareKeyAsync(Guid circleId, Guid memberId, byte[] sealedShareKey, byte[] circleShareKey)
    {
        var result = await table.SetSealedShareKeyAsync(circleId, memberId, sealedShareKey, circleShareKey);

        await InvalidateAsync(circleId, memberId);

        return result;
    }

    //

    /// <summary>Clears every member's sealed copy in a circle (<see cref="TableCircleMember.ClearSealedShareKeysAsync"/>).</summary>
    public async Task<int> ClearSealedShareKeysAsync(Guid circleId)
    {
        var members = await table.GetCircleMembersAsync(circleId);
        var result = await table.ClearSealedShareKeysAsync(circleId);

        await Cache.InvalidateAsync([
            ..members.SelectMany(m => new[]
            {
                Cache.CreateRemoveByKeyAction(GetCacheKey(circleId, m.memberId)),
                Cache.CreateRemoveByKeyAction(GetMemberCacheKey(m.memberId))
            }),
            Cache.CreateRemoveByKeyAction(GetCircleCacheKey(circleId)),
            Cache.CreateRemoveByKeyAction(CacheKeyAll)
        ]);

        return result;
    }

    //

    public async Task<CircleMemberRecord?> GetAsync(Guid circleId, Guid memberId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(circleId, memberId),
            _ => table.GetAsync(circleId, memberId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetListAsync(
            GetCircleCacheKey(circleId),
            _ => table.GetCircleMembersAsync(circleId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid memberId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetListAsync(
            GetMemberCacheKey(memberId),
            _ => table.GetMemberCirclesAndDataAsync(memberId),
            ttl ?? DefaultTtl);
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

    public async Task<List<CircleMemberRecord>> GetAllCirclesAsync(TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetListAsync(
            CacheKeyAll,
            _ => table.GetAllCirclesAsync(),
            ttl ?? DefaultTtl);
        return result;
    }

    //

}