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
    internal async Task<ClientRegistrationsRecord> GetAsync(Guid catId)
    {
        return await base.GetAsync(odinIdentity, catId);
    }

    internal new async Task<int> InsertAsync(ClientRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<int> UpsertAsync(ClientRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }
    
    internal async Task<int> DeleteAsync(Guid catId)
    {
        return await base.DeleteAsync(odinIdentity, catId);
    }

    internal async Task<List<ClientRegistrationsRecord>> GetCatsByTypeAsync(int catType)
    {
        return await base.GetCatsByTypeAsync(odinIdentity, catType);
    }
    
}