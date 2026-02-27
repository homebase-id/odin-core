using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableMySubscriptions(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableMySubscriptionsCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<MySubscriptionsRecord> GetAsync(OdinId sourceOwnerOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        return await base.GetAsync(odinIdentity, sourceOwnerOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId);
    }

    public new async Task<int> UpsertAsync(MySubscriptionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public async Task<int> DeleteAsync(OdinId sourceOwnerOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        return await base.DeleteAsync(odinIdentity, sourceOwnerOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId);
    }

    public async Task<List<MySubscriptionsRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }

    public async Task<int> DeleteBySourceOwnerOdinIdAsync(OdinId sourceOwnerOdinId)
    {
        return await base.DeleteBySourceOwnerOdinIdAsync(odinIdentity, sourceOwnerOdinId);
    }
}