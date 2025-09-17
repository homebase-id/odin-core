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

    internal async Task<DrivesRecord> GetAsync(Guid driveId)
    {
        return await base.GetByDriveIdAsync(odinIdentity, driveId);
    }

    internal async Task<List<DrivesRecord>> GetDrivesByTypeAsync(Guid driveType)
    {
        return await base.GetByDriveTypeAsync(odinIdentity, driveType);
    }

    internal new async Task<int> InsertAsync(DrivesRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<int> UpsertAsync(DrivesRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    internal async Task<(List<DrivesRecord>, UnixTimeUtc? nextCursor, long nextRowId)> GetList(int count, Int64? inCursor)
    {
        return await base.PagingByCreatedAsync(count, odinIdentity, inCursor, null);
    }

    internal async Task<DrivesRecord> GetByTargetDriveAsync(Guid driveAlias, Guid driveType)
    {
        return await base.GetByTargetDriveAsync(odinIdentity, driveAlias, driveType);
    }

    internal new async Task<int> GetCountAsync()
    {
        return await base.GetCountAsync();
    }

}