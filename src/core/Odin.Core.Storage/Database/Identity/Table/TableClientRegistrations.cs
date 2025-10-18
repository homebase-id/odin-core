using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableClientRegistrations(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableClientRegistrationsCRUD(scopedConnectionFactory)
{
    public async Task<ClientRegistrationsRecord> GetAsync(Guid catId)
    {
        return await base.GetAsync(odinIdentity, catId);
    }

    internal new async Task<int> InsertAsync(ClientRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(ClientRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }
    
    public async Task<int> DeleteAsync(Guid catId)
    {
        return await base.DeleteAsync(odinIdentity, catId);
    }

    public async Task<List<ClientRegistrationsRecord>> GetCatsByTypeAsync(int catType)
    {
        return await base.GetByTypeAsync(odinIdentity, catType);
    }
    public async Task<List<ClientRegistrationsRecord>> GetByTypeAndCategoryIdAsync(int catType, Guid categoryId)
    {
        return await base.GetByTypeAndCategoryIdAsync(odinIdentity, catType, categoryId);
    }

}