using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircleMember(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCircleMemberCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    internal async Task<int> DeleteAsync(Guid circleId, Guid memberId)
    {
        return await base.DeleteAsync(odinIdentity, circleId, memberId);
    }

    internal new async Task<int> InsertAsync(CircleMemberRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    /// <summary>
    /// Writes a membership row, never its sealed share key: the stored one is carried over, so refreshing a
    /// member's grant doesn't take their copy of the circle key.  Only <see cref="SetSealedShareKeyAsync"/>
    /// writes it (docs/selective-share-plan.md).
    /// </summary>
    internal new async Task<int> UpsertAsync(CircleMemberRecord item)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        var n = await UpsertKeepingSealedShareKeyAsync(cn, item);
        tx.Commit();
        return n;
    }

    private async Task<int> UpsertKeepingSealedShareKeyAsync(IConnectionWrapper cn, CircleMemberRecord item)
    {
        item.identityId = odinIdentity;

        // Locked on Postgres, so a sealed copy stored by another request can't land between this read and the write.
        await using (var cmd = cn.CreateCommand())
        {
            var forUpdate = cn.DatabaseType == DatabaseType.Sqlite ? "" : " FOR UPDATE";
            cmd.CommandText =
                "SELECT sealedShareKey FROM CircleMember " +
                $"WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId{forUpdate};";
            cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
            cmd.AddParameter("@circleId", DbType.Binary, item.circleId);
            cmd.AddParameter("@memberId", DbType.Binary, item.memberId);
            var stored = await cmd.ExecuteScalarAsync();
            item.sealedShareKey = stored is byte[] key ? key : null;
        }

        return await base.UpsertAsync(item);
    }

    /// <summary>
    /// Stores a member's sealed copy of the circle key, replacing any earlier one (the app re-seals when the
    /// member's drive key changes): 1 when stored, 0 when they are not a member of the circle.  Touches no
    /// other column.
    /// </summary>
    internal async Task<int> SetSealedShareKeyAsync(Guid circleId, Guid memberId, byte[] sealedShareKey)
    {
        // The column's bound (TableCircleMemberCRUD), and never empty: an empty copy would count as sealed.
        if (sealedShareKey == null || sealedShareKey.Length == 0 || sealedShareKey.Length > 2048)
        {
            throw new OdinDatabaseValidationException(
                $"sealedShareKey must be 1 to 2048 bytes, was {sealedShareKey?.Length.ToString() ?? "null"}");
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
            await UpsertKeepingSealedShareKeyAsync(cn, circleMember);
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