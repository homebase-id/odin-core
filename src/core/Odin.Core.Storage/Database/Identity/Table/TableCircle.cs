using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircle(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCircleCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>The bound of <c>appKeyLockedShareKey</c> (TableCircleCRUD).</summary>
    public const int MaxShareKeyLength = 512;

    internal async Task<CircleRecord> GetAsync(Guid circleId)
    {
        return await base.GetAsync(odinIdentity, circleId);
    }

    internal new async Task<int> InsertAsync(CircleRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    /// <remarks>Never writes the share key: the generated upsert leaves it out (keepOnUpsert).</remarks>
    internal new async Task<int> UpsertAsync(CircleRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    /// <summary>
    /// Sets the circle's share key, once: 1 when it was set, 0 when the circle has one already or doesn't
    /// exist.  Two devices sharing with a new circle at the same moment can't each set a different key; the
    /// loser reads the winner's.  Touches no other column.
    /// </summary>
    internal async Task<int> SetShareKeyAsync(Guid circleId, byte[] appKeyLockedShareKey)
    {
        // Never empty: an empty key would count as set.
        if (appKeyLockedShareKey == null || appKeyLockedShareKey.Length == 0 || appKeyLockedShareKey.Length > MaxShareKeyLength)
        {
            throw new OdinDatabaseValidationException(
                $"appKeyLockedShareKey must be 1 to {MaxShareKeyLength} bytes, was {appKeyLockedShareKey?.Length.ToString() ?? "null"}");
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