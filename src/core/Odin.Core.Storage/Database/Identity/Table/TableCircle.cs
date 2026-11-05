using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
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

    internal new async Task<int> UpsertAsync(CircleRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
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