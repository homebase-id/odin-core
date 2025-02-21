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
    public async Task<ConnectionsRecord> GetAsync(OdinId identity)
    {
        return await base.GetAsync(identityKey, identity);
    }

    public new async Task<int> InsertAsync(ConnectionsRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(ConnectionsRecord item)
    {
        item.identityId = identityKey;
        return await base.UpsertAsync(item);
    }

    public new async Task<int> UpdateAsync(ConnectionsRecord item)
    {
        item.identityId = identityKey;
        return await base.UpdateAsync(item);
    }

    public async Task<int> DeleteAsync(OdinId identity)
    {
        return await base.DeleteAsync(identityKey, identity);
    }

    public async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, string inCursor)
    {
        return await base.PagingByIdentityAsync(count, identityKey, inCursor);
    }

    public async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, Int32 status, string inCursor)
    {
        return await base.PagingByIdentityAsync(count, identityKey, status, inCursor);
    }


    public async Task<(List<ConnectionsRecord>, UnixTimeUtc?, long)> PagingByCreatedAsync(int count, Int32 status, UnixTimeUtc? inCursor, long rowId)
    {
        return await base.PagingByCreatedAsync(count, identityKey, status, inCursor, rowId);
    }

    public async Task<(List<ConnectionsRecord>, UnixTimeUtc?, long)> PagingByCreatedAsync(int count, UnixTimeUtc? inCursor, long rowId)
    {
        return await base.PagingByCreatedAsync(count, identityKey, inCursor, rowId);
    }
}
