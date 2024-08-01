using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Util;
using Org.BouncyCastle.Asn1.Ocsp;

namespace Odin.Services.Drives.Reactions.Group;

public class GroupReactionService(
    DriveDatabaseHost driveDatabaseHost,
    IMediator mediator,
    TenantContext tenantContext,
    FileSystemResolver fileSystemResolver)
{
    public async Task<AddReactionResult> AddReaction(FileIdentifier fileId, string reaction, ReactionTransitOptions options, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);
        var callerId = odinContext.GetCallerOdinIdOrFail();

        var result = new AddReactionResult();
        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, connection);
        if (manager != null)
        {
            manager.AddReaction(callerId, file.FileId, reaction, connection);

            if (options?.Recipients?.Any() ?? false)
            {
                //TODO: enqueue in outbox and update result
            }

            await mediator.Publish(new ReactionContentAddedNotification
            {
                Reaction = new Reaction()
                {
                    OdinId = callerId,
                    Created = UnixTimeUtcUnique.Now(), //TODO: i should technically pull this from the db records
                    ReactionContent = reaction,
                    FileId = file
                },
                OdinContext = odinContext,
                DatabaseConnection = connection
            });
        }

        return result;
    }

    public async Task<DeleteReactionResult> DeleteReaction(FileIdentifier fileId, string reaction, ReactionTransitOptions options, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var result = new DeleteReactionResult();

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, connection);
        if (manager != null)
        {
            manager.DeleteReaction(odinContext.GetCallerOdinIdOrFail(), file.FileId, reaction, connection);

            if (options?.Recipients?.Any() ?? false)
            {
                //TODO: enqueue in outbox and update result
            }

            await mediator.Publish(new ReactionDeletedNotification
            {
                Reaction = new Reaction()
                {
                    OdinId = odinContext.GetCallerOdinIdOrFail(),
                    Created = default,
                    ReactionContent = reaction,
                    FileId = file
                },
                OdinContext = odinContext,
                DatabaseConnection = connection
            });
        }

        return result;
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(FileIdentifier fileId, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, connection);
        if (manager != null)
        {
            var (reactions, total) = manager.GetReactionSummaryByFile(file.FileId, connection);

            return new GetReactionCountsResponse()
            {
                Reactions = reactions,
                Total = total
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId identity, FileIdentifier fileId, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertIsValidOdinId(identity, out _);

        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, connection);
        if (manager != null)
        {
            var result = manager.GetReactionsByIdentityAndFile(identity, file.FileId, connection);
            return result;
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public async Task<GetReactionsResponse> GetReactions(FileIdentifier fileId, int cursor, int maxCount, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        var manager = await driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, connection);
        if (manager != null)
        {
            var (list, nextCursor) =
                manager.GetReactionsByFile(maxCount, cursor, file.FileId, connection);

            return new GetReactionsResponse()
            {
                Reactions = list,
                Cursor = nextCursor
            };
        }

        throw new OdinSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    //

    private async Task<InternalDriveFileId> GetLocalFileId(FileIdentifier fileId, IOdinContext odinContext, DatabaseConnection connection,
        FileSystemType fileSystemType)
    {
        var fs = fileSystemResolver.ResolveFileSystem(fileSystemType);
        var localFileId = (await fs.Query.ResolveFileId(fileId.ToGlobalTransitIdFileIdentifier(), odinContext, connection)).GetValueOrDefault();

        if (!localFileId.IsValid())
        {
            throw new OdinClientException("No local file found by the global transit id", OdinClientErrorCode.InvalidFile);
        }

        return localFileId;
    }
}