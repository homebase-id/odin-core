using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyTwoValue(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableKeyTwoValueCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(byte[] key2)
    {
        return await base.GetByKeyTwoAsync(identityKey, key2);
    }

    public async Task<KeyTwoValueRecord> GetAsync(byte[] key1)
    {
        return await base.GetAsync(identityKey, key1);
    }

    public async Task<int> DeleteAsync(byte[] key1)
    {
        return await base.DeleteAsync(identityKey, key1);
    }

    public override async Task<int> InsertAsync(KeyTwoValueRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public override async Task<int> UpsertAsync(KeyTwoValueRecord item)
    {
        item.identityId = identityKey;
        return await base.UpsertAsync(item);
    }

    public override async Task<int> UpdateAsync(KeyTwoValueRecord item)
    {
        item.identityId = identityKey;
        return await base.UpdateAsync(item);
    }
}