using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

[assembly: InternalsVisibleTo("IdentityDatabase")]

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveDefinitions(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveDefinitionsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<DriveDefinitionsRecord> GetAsync(Guid driveId)
    {
        return await base.GetByDriveIdAsync(odinIdentity, driveId);
    }

    public async Task<List<DriveDefinitionsRecord>> GetDrivesByType(Guid driveType)
    {
        return await base.GetByDriveTypeAsync(odinIdentity, driveType);
    }

    public new async Task<int> UpsertAsync(DriveDefinitionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public async Task<(List<DriveDefinitionsRecord>, UnixTimeUtc? nextCursor, long nextRowId)> GetList(int count, Int64? inCursor)
    {
        return await base.PagingByCreatedAsync(count, odinIdentity, inCursor, null);
    }
}