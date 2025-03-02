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


    public async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(int count, Int32 status, string cursorString)
    {
        var cursor = TimeRowCursor.FromJsonOrOldString(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, identityKey, status, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }

    public async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursorString)
    {
        var cursor = TimeRowCursor.FromJsonOrOldString(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, identityKey, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }
}
