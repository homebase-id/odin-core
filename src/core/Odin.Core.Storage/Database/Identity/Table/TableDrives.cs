using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDrives(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDrivesCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<DrivesRecord> GetAsync(Guid driveId)
    {
        return await base.GetByDriveIdAsync(odinIdentity, driveId);
    }

    public async Task<List<DrivesRecord>> GetDrivesByType(Guid driveType)
    {
        return await base.GetByDriveTypeAsync(odinIdentity, driveType);
    }

    public new async Task<int> InsertAsync(DrivesRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(DrivesRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public async Task<(List<DrivesRecord>, UnixTimeUtc? nextCursor, long nextRowId)> GetList(int count, Int64? inCursor)
    {
        return await base.PagingByCreatedAsync(count, odinIdentity, inCursor, null);
    }

    public async Task<DrivesRecord> GetByTargetDriveAsync(Guid driveAlias, Guid driveType)
    {
        return await base.GetByTargetDriveAsync(odinIdentity, driveAlias, driveType);
    }

    public new async Task<int> GetCountAsync()
    {
        return await base.GetCountAsync();
    }

}