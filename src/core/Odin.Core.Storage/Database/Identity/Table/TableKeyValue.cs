using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyValue(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableKeyValueCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<KeyValueRecord> GetAsync(byte[] key)
    {
        return await base.GetAsync(identityKey, key);
    }

    public new async Task<int> InsertAsync(KeyValueRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(byte[] key)
    {
        return await base.DeleteAsync(identityKey, key);
    }

    public new async Task<int> UpsertAsync(KeyValueRecord item)
    {
        item.identityId = identityKey;
        return await base.UpsertAsync(item);
    }

    public async Task<int> UpsertManyAsync(List<KeyValueRecord> items)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var affectedRows = 0;
        foreach (var item in items)
        {
            item.identityId = identityKey;
            affectedRows += await base.UpsertAsync(item);
        }

        tx.Commit();

        return affectedRows;
    }

    public new async Task<int> UpdateAsync(KeyValueRecord item)
    {
        item.identityId = identityKey;
        return await base.UpdateAsync(item);
    }
}