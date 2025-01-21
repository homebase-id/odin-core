using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveTransferHistory(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableDriveTransferHistoryCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<List<DriveTransferHistoryRecord>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(identityKey, driveId, fileId);
    }

    public async Task<DriveTransferHistoryRecord> GetAsync(Guid driveId, Guid fileId, OdinId remoteIdentityId)
    {
        return await base.GetAsync(identityKey, driveId, fileId, remoteIdentityId);
    }

    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(identityKey, driveId, fileId);
    }

    public new async Task UpsertAsync(DriveTransferHistoryRecord r)
    {
        if (r == null)
            return;

        if (r.identityId != identityKey)
            throw new ArgumentException($"The identity ID does not match the expected value. Expected: {r.identityId}, Actual: {identityKey}", nameof(r.identityId));

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await base.UpsertAsync(r);
    }
}