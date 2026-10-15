using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableBundleTokenApps(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableBundleTokenAppsCRUD(scopedConnectionFactory)
{
    public async Task<List<BundleTokenAppsRecord>> GetByTokenIdAsync(Guid tokenId)
    {
        return await base.GetByTokenIdAsync(odinIdentity, tokenId);
    }

    public async Task<List<BundleTokenAppsRecord>> GetByAppIdAsync(Guid appId)
    {
        return await base.GetByAppIdAsync(odinIdentity, appId);
    }

    public async Task<List<BundleTokenAppsRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }

    public new async Task<int> InsertAsync(BundleTokenAppsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(Guid tokenId, Guid appId)
    {
        return await base.DeleteAsync(odinIdentity, tokenId, appId);
    }

    public async Task<int> DeleteByTokenIdAsync(Guid tokenId)
    {
        return await base.DeleteByTokenIdAsync(odinIdentity, tokenId);
    }

    public async Task<(List<BundleTokenAppsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}
