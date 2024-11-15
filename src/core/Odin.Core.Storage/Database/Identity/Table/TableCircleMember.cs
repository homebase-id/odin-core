using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircleMember(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableCircleMemberCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;
    public Guid IdentityId { get; } = identityKey.Id;

    public async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        return await base.DeleteAsync(IdentityId, circleId, memberId);
    }

    public override async Task<int> InsertAsync(CircleMemberRecord item)
    {
        item.identityId = IdentityId;
        return await base.InsertAsync(item);
    }

    public override async Task<int> UpsertAsync(CircleMemberRecord item)
    {
        item.identityId = IdentityId;
        return await base.UpsertAsync(item);
    }

    public async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId)
    {
        // The services code doesn't handle null, so I've made this override
        var r = await base.GetCircleMembersAsync(IdentityId, circleId) ?? [];
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
        var r = await base.GetMemberCirclesAndDataAsync(IdentityId, memberId) ?? [];
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
            circleMember.identityId = IdentityId;
            await base.UpsertAsync(circleMember);
        }

        await tx.CommitAsync();
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
            await base.DeleteAsync(IdentityId, circleId, member);

        await tx.CommitAsync();
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
            var circles = await base.GetMemberCirclesAndDataAsync(IdentityId, member);

            foreach (var circle in circles)
                await base.DeleteAsync(IdentityId, circle.circleId, member);
        }

        await tx.CommitAsync();
    }
}
