using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyValue(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableKeyValueCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    internal async Task<KeyValueRecord> GetAsync(byte[] key)
    {
        return await base.GetAsync(odinIdentity, key);
    }

    internal new async Task<int> InsertAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<bool> TryInsertAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    internal async Task<int> DeleteAsync(byte[] key)
    {
        return await base.DeleteAsync(odinIdentity, key);
    }

    internal new async Task<int> UpsertAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    internal async Task<int> UpsertManyAsync(List<KeyValueRecord> items)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var affectedRows = 0;
        foreach (var item in items)
        {
            item.identityId = odinIdentity;
            affectedRows += await base.UpsertAsync(item);
        }

        tx.Commit();

        return affectedRows;
    }

    internal new async Task<int> UpdateAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }
}