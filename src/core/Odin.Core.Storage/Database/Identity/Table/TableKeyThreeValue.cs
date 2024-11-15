using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyThreeValue(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableKeyThreeValueCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public Guid IdentityId { get; } = identityKey.Id;

    public async Task<KeyThreeValueRecord> GetAsync(byte[] key1)
    {
        return await base.GetAsync(IdentityId, key1);
    }

    public async Task<List<byte[]>> GetByKeyTwoAsync(byte[] key2)
    {
        return await base.GetByKeyTwoAsync(IdentityId, key2);
    }

    public async Task<List<byte[]>> GetByKeyThreeAsync(byte[] key3)
    {
        return await base.GetByKeyThreeAsync(IdentityId, key3);
    }

    public async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(byte[] key2, byte[] key3)
    {
        return await base.GetByKeyTwoThreeAsync(IdentityId, key2, key3);
    }

    public override async Task<int> UpsertAsync(KeyThreeValueRecord item)
    {
        item.identityId = IdentityId;
        return await base.UpsertAsync(item);
    }

    public override async Task<int> InsertAsync(KeyThreeValueRecord item)
    {
        item.identityId = IdentityId;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(byte[] key1)
    {
        return await base.DeleteAsync(IdentityId, key1);
    }

    public override async Task<int> UpdateAsync(KeyThreeValueRecord item)
    {
        item.identityId = IdentityId;
        return await base.UpdateAsync(item);
    }

}