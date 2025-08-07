using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircleMember(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCircleMemberCRUD(cache, scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        return await base.DeleteAsync(odinIdentity, circleId, memberId);
    }

    public new async Task<int> InsertAsync(CircleMemberRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(CircleMemberRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId)
    {
        // The services code doesn't handle null, so I've made this override
        var r = await base.GetCircleMembersAsync(odinIdentity, circleId) ?? [];
        return r;
    }

    /// <summary>
    /// Returns all members of the given circle (the data, aka exchange grants not returned)
    /// </summary>
    /// <param name="memberId"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid memberId)
    {
        // The services code doesn't handle null, so I've made this override
        var r = await base.GetMemberCirclesAndDataAsync(odinIdentity, memberId) ?? [];
        return r;
    }

    /// <summary>
    /// Adds each CircleMemberRecord in the supplied list.
    /// </summary>
    /// <param name="circleMemberRecordList"></param>
    /// <exception cref="Exception"></exception>
    public async Task UpsertCircleMembersAsync(List<CircleMemberRecord> circleMemberRecordList)
    {
        if (circleMemberRecordList == null || circleMemberRecordList.Count < 1)
            throw new OdinSystemException("No members supplied (null or empty)");

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var circleMember in circleMemberRecordList)
        {
            circleMember.identityId = odinIdentity;
            await base.UpsertAsync(circleMember);
        }

        tx.Commit();
    }

    /// <summary>
    /// Removes the list of members from the given circle.
    /// </summary>
    /// <param name="circleId"></param>
    /// <param name="members"></param>
    /// <exception cref="Exception"></exception>
    public async Task RemoveCircleMembersAsync(Guid circleId, List<Guid> members)
    {
        if (members == null || members.Count < 1)
            throw new OdinSystemException("No members supplied (null or empty)");

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var member in members)
            await base.DeleteAsync(odinIdentity, circleId, member);

        tx.Commit();
    }


    /// <summary>
    /// Removes the supplied list of members from all circles.
    /// </summary>
    /// <param name="members"></param>
    /// <exception cref="Exception"></exception>
    public async Task DeleteMembersFromAllCirclesAsync(List<Guid> members)
    {
        if (members == null || members.Count < 1)
            throw new OdinSystemException("No members supplied (null or empty)");

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var member in members)
        {
            var circles = await base.GetMemberCirclesAndDataAsync(odinIdentity, member);

            foreach (var circle in circles)
                await base.DeleteAsync(odinIdentity, circle.circleId, member);
        }

        tx.Commit();
    }

    public Task<List<CircleMemberRecord>> GetAllCirclesAsync()
    {
        return base.GetAllCirclesAsync(odinIdentity.Id);
    }
}