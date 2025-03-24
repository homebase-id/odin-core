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
    IdentityKey identityKey)
    : TableCircleCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public async Task<CircleRecord> GetAsync(Guid circleId)
    {
        return await base.GetAsync(identityKey, circleId);
    }

    public new async Task<int> InsertAsync(CircleRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(Guid circleId)
    {
        return await base.DeleteAsync(identityKey, circleId);
    }

    public async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(int count, Guid? inCursor)
    {
        return await PagingByCircleIdAsync(count, identityKey, inCursor);
    }
}
