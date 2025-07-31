using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCircle(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCircleCRUD(cache, scopedConnectionFactory)
{
    public async Task<CircleRecord> GetAsync(Guid circleId)
    {
        return await base.GetAsync(odinIdentity, circleId);
    }

    public new async Task<int> InsertAsync(CircleRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(Guid circleId)
    {
        return await base.DeleteAsync(odinIdentity, circleId);
    }

    public async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(int count, Guid? inCursor)
    {
        return await PagingByCircleIdAsync(count, odinIdentity, inCursor);
    }
}
