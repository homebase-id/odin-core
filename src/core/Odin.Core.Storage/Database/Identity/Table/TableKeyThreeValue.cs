using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyThreeValue(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableKeyThreeValueCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public async Task<KeyThreeValueRecord> GetAsync(byte[] key1)
    {
        return await base.GetAsync(odinIdentity, key1);
    }

    public async Task<List<byte[]>> GetByKeyTwoAsync(byte[] key2)
    {
        return await base.GetByKeyTwoAsync(odinIdentity, key2);
    }

    public async Task<List<byte[]>> GetByKeyThreeAsync(byte[] key3)
    {
        return await base.GetByKeyThreeAsync(odinIdentity, key3);
    }

    public async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(byte[] key2, byte[] key3)
    {
        return await base.GetByKeyTwoThreeAsync(odinIdentity, key2, key3);
    }

    public new async Task<int> UpsertAsync(KeyThreeValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public new async Task<int> InsertAsync(KeyThreeValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(byte[] key1)
    {
        return await base.DeleteAsync(odinIdentity, key1);
    }

    public new async Task<int> UpdateAsync(KeyThreeValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

}