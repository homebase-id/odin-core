using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableConnections(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableConnectionsCRUD(scopedConnectionFactory)
{
    internal async Task<ConnectionsRecord> GetAsync(OdinId identity)
    {
        return await base.GetAsync(odinIdentity, identity);
    }

    internal new async Task<int> InsertAsync(ConnectionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<int> UpsertAsync(ConnectionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    internal new async Task<int> UpdateAsync(ConnectionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    internal async Task<int> DeleteAsync(OdinId identity)
    {
        return await base.DeleteAsync(odinIdentity, identity);
    }

    internal async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, string inCursor)
    {
        return await base.PagingByIdentityAsync(count, odinIdentity, inCursor);
    }

    internal async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(int count, Int32 status, string inCursor)
    {
        return await base.PagingByIdentityAsync(count, odinIdentity, status, inCursor);
    }


    internal async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(int count, Int32 status, string cursorString)
    {
        var cursor = TimeRowCursor.FromJson(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, odinIdentity, status, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }

    internal async Task<(List<ConnectionsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursorString)
    {
        var cursor = TimeRowCursor.FromJson(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, odinIdentity, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }
}
