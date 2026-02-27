using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableMySubscribers(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableMySubscribersCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<MySubscribersRecord> GetAsync(OdinId subscriberOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        return await base.GetAsync(odinIdentity, subscriberOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId);
    }

    public new async Task<int> UpsertAsync(MySubscribersRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public async Task<int> DeleteAsync(OdinId subscriberOdinId, Guid sourceDriveId, Guid sourceDriveTypeId, Guid targetDriveId)
    {
        return await base.DeleteAsync(odinIdentity, subscriberOdinId, sourceDriveId, sourceDriveTypeId, targetDriveId);
    }

    public async Task<List<MySubscribersRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }

    public async new Task<int> DeleteBySubscriberOdinIdAsync(Guid identityId, OdinId subscriberOdinId)
    {
        return await base.DeleteBySubscriberOdinIdAsync(identityId, subscriberOdinId);
    }
}