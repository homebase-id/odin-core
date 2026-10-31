using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircleMember(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCircleMemberCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>The bound of <c>sealedShareKey</c> (TableCircleMemberCRUD).</summary>
    public const int MaxSealedShareKeyLength = 2048;

    internal async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        return await base.DeleteAsync(odinIdentity, circleId, memberId);
    }

    internal new async Task<int> InsertAsync(CircleMemberRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    /// <remarks>Never writes the sealed share key: the generated upsert leaves it out (keepOnUpsert).</remarks>
    internal new async Task<int> UpsertAsync(CircleMemberRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    /// <summary>
    /// Stores a member's sealed copy of the circle key, replacing any earlier one (the app re-seals when the
    /// member's drive key changes): 1 when stored, 0 when they are not a member of the circle.  Touches no
    /// other column.
    /// </summary>
    internal async Task<int> SetSealedShareKeyAsync(Guid circleId, Guid memberId, byte[] sealedShareKey)
    {
        // Never empty: an empty copy would count as sealed.
        if (sealedShareKey == null || sealedShareKey.Length == 0 || sealedShareKey.Length > MaxSealedShareKeyLength)
        {
            throw new OdinDatabaseValidationException(
                $"sealedShareKey must be 1 to {MaxSealedShareKeyLength} bytes, was {sealedShareKey?.Length.ToString() ?? "null"}");
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "UPDATE CircleMember SET sealedShareKey = @sealedShareKey " +
            "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId;";
        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        cmd.AddParameter("@circleId", DbType.Binary, circleId);
        cmd.AddParameter("@memberId", DbType.Binary, memberId);
        cmd.AddParameter("@sealedShareKey", DbType.Binary, sealedShareKey);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<CircleMemberRecord> GetAsync(Guid circleId, Guid memberId)
    {
        return await base.GetAsync(odinIdentity, circleId, memberId);
    }

    internal async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId)
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
    internal async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid memberId)
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
    internal async Task UpsertCircleMembersAsync(List<CircleMemberRecord> circleMemberRecordList)
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
    internal async Task RemoveCircleMembersAsync(Guid circleId, List<Guid> members)
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
    internal async Task DeleteMembersFromAllCirclesAsync(List<Guid> members)
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

    internal Task<List<CircleMemberRecord>> GetAllCirclesAsync()
    {
        return base.GetAllCirclesAsync(odinIdentity.IdentityId);
    }

    public async Task<(List<CircleMemberRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}