using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableConnections(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableConnectionsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public Guid IdentityId { get; } = identityKey.Id;

    public async Task<ConnectionsRecord> GetAsync(OdinId identity)
    {
        return await base.GetAsync(IdentityId, identity);
    }

    public override async Task<int> InsertAsync(ConnectionsRecord item)
    {
        item.identityId = IdentityId;
        return await base.InsertAsync(item);
    }

    public override async Task<int> UpsertAsync(ConnectionsRecord item)
    {
        item.identityId = IdentityId;
        return await base.UpsertAsync(item);
    }

    public override async Task<int> UpdateAsync(ConnectionsRecord item)
    {
        item.identityId = IdentityId;
        return await base.UpdateAsync(item);
    }

    public async Task<int> DeleteAsync(OdinId identity)
    {
        return await base.DeleteAsync(IdentityId, identity);
    }

    public async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, string inCursor)
    {
        return await base.PagingByIdentityAsync(count, IdentityId, inCursor);
    }

    public async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, Int32 status, string inCursor)
    {
        return await base.PagingByIdentityAsync(count, IdentityId, status, inCursor);
    }


    public async Task<(List<ConnectionsRecord>, UnixTimeUtcUnique?)> PagingByCreatedAsync(int count, Int32 status, UnixTimeUtcUnique? inCursor)
    {
        return await base.PagingByCreatedAsync(count, IdentityId, status, inCursor);
    }

    public async Task<(List<ConnectionsRecord>, UnixTimeUtcUnique?)> PagingByCreatedAsync(int count, UnixTimeUtcUnique? inCursor)
    {
        return await base.PagingByCreatedAsync(count, IdentityId, inCursor);
    }
}
