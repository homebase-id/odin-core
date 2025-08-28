using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyTwoValue(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableKeyTwoValueCRUD(scopedConnectionFactory)
{
    public async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(byte[] key2)
    {
        return await base.GetByKeyTwoAsync(odinIdentity, key2);
    }

    public async Task<KeyTwoValueRecord> GetAsync(byte[] key1)
    {
        return await base.GetAsync(odinIdentity, key1);
    }

    public async Task<int> DeleteAsync(byte[] key1)
    {
        return await base.DeleteAsync(odinIdentity, key1);
    }

    public new async Task<int> InsertAsync(KeyTwoValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(KeyTwoValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public new async Task<int> UpdateAsync(KeyTwoValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }
}