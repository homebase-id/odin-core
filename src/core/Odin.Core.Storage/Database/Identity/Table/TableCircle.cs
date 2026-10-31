using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircle(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCircleCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    internal async Task<CircleRecord> GetAsync(Guid circleId)
    {
        return await base.GetAsync(odinIdentity, circleId);
    }

    internal new async Task<int> InsertAsync(CircleRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    /// <summary>
    /// Writes a circle's definition, never its share key: the stored key is carried over, so renaming,
    /// disabling or regranting a circle can't lose the key its shared files are locked with.  Only
    /// <see cref="SetShareKeyAsync"/> writes it (docs/selective-share-plan.md).
    /// </summary>
    internal new async Task<int> UpsertAsync(CircleRecord item)
    {
        item.identityId = odinIdentity;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        // Locked on Postgres, so a share key set by another request can't land between this read and the write.
        await using (var cmd = cn.CreateCommand())
        {
            var forUpdate = cn.DatabaseType == DatabaseType.Sqlite ? "" : " FOR UPDATE";
            cmd.CommandText =
                $"SELECT appKeyLockedShareKey FROM Circle WHERE identityId = @identityId AND circleId = @circleId{forUpdate};";
            cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
            cmd.AddParameter("@circleId", DbType.Binary, item.circleId);
            var stored = await cmd.ExecuteScalarAsync();
            item.appKeyLockedShareKey = stored is byte[] key ? key : null;
        }

        var n = await base.UpsertAsync(item);
        tx.Commit();
        return n;
    }

    /// <summary>
    /// Sets the circle's share key, once: 1 when it was set, 0 when the circle has one already or doesn't
    /// exist.  Two devices sharing with a new circle at the same moment can't each set a different key; the
    /// loser reads the winner's.  Touches no other column.
    /// </summary>
    internal async Task<int> SetShareKeyAsync(Guid circleId, byte[] appKeyLockedShareKey)
    {
        // The column's bound (TableCircleCRUD), and never empty: an empty key would count as set.
        if (appKeyLockedShareKey == null || appKeyLockedShareKey.Length == 0 || appKeyLockedShareKey.Length > 512)
        {
            throw new OdinDatabaseValidationException(
                $"appKeyLockedShareKey must be 1 to 512 bytes, was {appKeyLockedShareKey?.Length.ToString() ?? "null"}");
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "UPDATE Circle SET appKeyLockedShareKey = @appKeyLockedShareKey " +
            "WHERE identityId = @identityId AND circleId = @circleId AND appKeyLockedShareKey IS NULL;";
        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        cmd.AddParameter("@circleId", DbType.Binary, circleId);
        cmd.AddParameter("@appKeyLockedShareKey", DbType.Binary, appKeyLockedShareKey);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> DeleteAsync(Guid circleId)
    {
        return await base.DeleteAsync(odinIdentity, circleId);
    }

    /// <summary>
    /// Circles whose owning app wants members enrolled at the given moment.  Hits Idx1Circle
    /// (identityId, GrantOn) -- this query is the reason GrantOn is a column and not a blob field.
    /// </summary>
    internal async Task<List<CircleRecord>> GetByGrantOnAsync(int grantOn)
    {
        return await base.GetByGrantOnAsync(odinIdentity, grantOn) ?? [];
    }

    /// <summary>
    /// The circle an app holds under a slug, or null.  Slugs are unique per owning app (#1939).
    /// </summary>
    public async Task<CircleRecord> GetByAppSlugAsync(Guid appId, string circleSlug)
    {
        return await base.GetByAppSlugAsync(odinIdentity, appId, circleSlug);
    }

    /// <summary>
    /// Every circle for this identity.  Circles number in the tens, so this is a single read rather
    /// than a paged one -- the paging overload is still there for callers that want it.
    /// </summary>
    internal async Task<List<CircleRecord>> GetAllAsync()
    {
        var results = new List<CircleRecord>();
        Guid? cursor = null;

        do
        {
            var (page, next) = await PagingByCircleIdAsync(256, cursor);
            results.AddRange(page);
            cursor = next;
        } while (cursor != null);

        return results;
    }

    internal async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(int count, Guid? inCursor)
    {
        return await PagingByCircleIdAsync(count, odinIdentity, inCursor);
    }

    public async Task<(List<CircleRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}