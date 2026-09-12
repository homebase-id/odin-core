using System;
using System.Collections.Generic;
using System.Data;
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

    internal new async Task<bool> TryInsertAsync(DrivesRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
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

    /// <summary>
    /// Number of drives belonging to THIS identity.
    /// </summary>
    /// <remarks>
    /// Deliberately does not call base.GetCountAsync(): the generated CRUD emits a bare
    /// "SELECT COUNT(*) FROM Drives" with no identityId filter. That is accidentally correct on
    /// SQLite, where each tenant has its own database file, but on Postgres every tenant shares
    /// one database and the unfiltered count returns the drive count of the whole fleet.
    /// </remarks>
    internal new async Task<int> GetCountAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(*) FROM drives WHERE identityId=@identityId;";
        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);

        var count = await cmd.ExecuteScalarAsync();
        if (count == null || count == DBNull.Value || !(count is int || count is long))
        {
            return -1;
        }

        return Convert.ToInt32(count);
    }

    public async Task<(List<DrivesRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }


}