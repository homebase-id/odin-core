using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableBundleTokens(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableBundleTokensCRUD(scopedConnectionFactory)
{
    public async Task<BundleTokensRecord> GetAsync(Guid tokenId)
    {
        return await base.GetAsync(odinIdentity, tokenId);
    }

    public async Task<List<BundleTokensRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }

    public new async Task<int> InsertAsync(BundleTokensRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(BundleTokensRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public new async Task<int> UpdateAsync(BundleTokensRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    public async Task<int> DeleteAsync(Guid tokenId)
    {
        return await base.DeleteAsync(odinIdentity, tokenId);
    }

    public async Task<(List<BundleTokensRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}
