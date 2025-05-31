using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
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

    public new async Task<int> InsertAsync(DriveDefinitionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
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

    public async Task<DriveDefinitionsRecord> GetByTargetDrive(Guid driveAlias, Guid driveType)
    {
        return await base.GetByTargetDriveAsync(odinIdentity, driveAlias, driveType);
    }

    public async Task Temp_MigrateDriveMainIndex(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE driveMainIndex SET driveId = @newDriveId WHERE identityId = @identityId AND driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = odinIdentity.Id.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();
        await AssertUpdateSuccess(cn, "driveMainIndex", odinIdentity.Id, oldDriveId);
    }

    public async Task Temp_MigrateDriveLocalTagIndex(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;

        await using var command = cn.CreateCommand();
        command.CommandText =
            "UPDATE DriveLocalTagIndex SET driveId = @newDriveId WHERE identityId = @identityId AND driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "DriveLocalTagIndex", identityId, oldDriveId);
    }

    public async Task Temp_MigrateDriveAclIndex(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE DriveAclIndex SET driveId = @newDriveId WHERE identityId = @identityId AND driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "DriveAclIndex", identityId, oldDriveId);
    }

    public async Task Temp_MigrateDriveTransferHistory(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText =
            "UPDATE DriveTransferHistory SET driveId = @newDriveId WHERE identityId = @identityId AND driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "DriveTransferHistory", identityId, oldDriveId);
    }

    public async Task Temp_MigrateDriveReactions(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE DriveReactions SET driveId = @newDriveId WHERE identityId = @identityId AND driveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "DriveReactions", identityId, oldDriveId);
    }

    public async Task Temp_MigrateDriveTagIndex(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE DriveTagIndex SET DriveId = @newDriveId WHERE identityId = @identityId AND DriveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "DriveTagIndex", identityId, oldDriveId);
    }
    
    public async Task Temp_MigrateFollowsMe(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE FollowsMe SET DriveId = @newDriveId WHERE identityId = @identityId AND DriveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "FollowsMe", identityId, oldDriveId);
    }

    public async Task Temp_MigrateImFollowing(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE ImFollowing SET DriveId = @newDriveId WHERE identityId = @identityId AND DriveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "ImFollowing", identityId, oldDriveId);
    }
    
    public async Task Temp_MigrateInbox(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE Inbox SET boxId = @newDriveId WHERE identityId = @identityId AND boxId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await using var validateCommand = cn.CreateCommand();
        validateCommand.CommandText = $"SELECT COUNT(*) FROM Inbox WHERE identityId = @identityId AND boxId = @oldDriveId";

        var v1 = validateCommand.CreateParameter();
        v1.ParameterName = "@identityId";
        v1.Value = identityId.ToByteArray();
        validateCommand.Parameters.Add(v1);

        var v2 = validateCommand.CreateParameter();
        v2.ParameterName = "@oldDriveId";
        v2.Value = oldDriveId.ToByteArray();
        validateCommand.Parameters.Add(v2);

        var count = await validateCommand.ExecuteScalarAsync();
        if (Convert.ToInt32(count) > 0)
        {
            throw new OdinSystemException(
                $"Found {Convert.ToInt32(count)} rows remaining in table Inbox for old driveId {oldDriveId} on identityId {identityId}");
        }
    }
    
    public async Task Temp_MigrateOutbox(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE outbox SET DriveId = @newDriveId WHERE identityId = @identityId AND DriveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "outbox", identityId, oldDriveId);
    }
    
    public async Task Temp_MigrateDriveDefinitions(Guid oldDriveId, Guid driveAlias)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        var identityId = odinIdentity.Id;
        await using var command = cn.CreateCommand();
        command.CommandText = "UPDATE DriveDefinitions SET DriveId = @newDriveId WHERE identityId = @identityId AND DriveId = @oldDriveId";

        var p1 = command.CreateParameter();
        p1.ParameterName = "@newDriveId";
        p1.Value = driveAlias.ToByteArray();
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@identityId";
        p2.Value = identityId.ToByteArray();
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@oldDriveId";
        p3.Value = oldDriveId.ToByteArray();
        command.Parameters.Add(p3);

        await command.ExecuteNonQueryAsync();

        await AssertUpdateSuccess(cn, "DriveDefinitions", identityId, oldDriveId);
    }
    
    private static async Task AssertUpdateSuccess(IConnectionWrapper cn, string tableName, Guid identityId, Guid oldDriveId)
    {
        await using var validateCommand = cn.CreateCommand();
        validateCommand.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE identityId = @identityId AND driveId = @oldDriveId";

        var v1 = validateCommand.CreateParameter();
        v1.ParameterName = "@identityId";
        v1.Value = identityId.ToByteArray();
        validateCommand.Parameters.Add(v1);

        var v2 = validateCommand.CreateParameter();
        v2.ParameterName = "@oldDriveId";
        v2.Value = oldDriveId.ToByteArray();
        validateCommand.Parameters.Add(v2);

        var count = await validateCommand.ExecuteScalarAsync();
        if (Convert.ToInt32(count) > 0)
        {
            throw new OdinSystemException(
                $"Found {Convert.ToInt32(count)} rows remaining in table {tableName} for old driveId {oldDriveId} on identityId {identityId}");
        }
    }
}