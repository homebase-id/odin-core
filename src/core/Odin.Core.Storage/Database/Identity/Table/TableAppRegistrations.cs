using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppRegistrations(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppRegistrationsCRUD(scopedConnectionFactory)
{
    public async Task<AppRegistrationsRecord> GetAsync(Guid appId)
    {
        return await base.GetAsync(odinIdentity, appId);
    }

    /// <summary>
    /// Resolves an app by the slug a remote caller used to address it.
    /// </summary>
    public async Task<AppRegistrationsRecord> GetByAppSlugAsync(string appSlug)
    {
        return await base.GetByAppSlugAsync(odinIdentity, appSlug);
    }

    public async Task<List<AppRegistrationsRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }

    public new async Task<int> InsertAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<bool> TryInsertAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    public new async Task<int> UpsertAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public new async Task<int> UpdateAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    public async Task<int> DeleteAsync(Guid appId)
    {
        return await base.DeleteAsync(odinIdentity, appId);
    }

    public async Task<(List<AppRegistrationsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}
