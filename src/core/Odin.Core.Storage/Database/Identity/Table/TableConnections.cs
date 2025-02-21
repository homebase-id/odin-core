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


    public async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(int count, Int32 status, string cursor)
    {
        UnixTimeUtc? utc = null;

        if (MainIndexMeta.TryParseModifiedCursor(cursor, out var ts, out var rowId))
            utc = new UnixTimeUtc(ts);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, identityKey, status, utc, rowId);

        return (r, tsc == null ? null : tsc.ToString() + "," + ri.ToString());
    }

    public async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursor)
    {
        UnixTimeUtc? utc = null;

        if (MainIndexMeta.TryParseModifiedCursor(cursor, out var ts, out var rowId))
            utc = new UnixTimeUtc(ts);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, identityKey, utc, rowId);

        return (r, tsc == null ? null : tsc.ToString() + "," + ri.ToString());
    }
}
