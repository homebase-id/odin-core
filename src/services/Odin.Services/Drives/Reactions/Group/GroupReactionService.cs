using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Util;

namespace Odin.Services.Drives.Reactions.Group;

public class GroupReactionService(
    TenantContext tenantContext,
    ReactionContentService reactionContentService,
    FileSystemResolver fileSystemResolver)
{
    public async Task<AddReactionResult> AddReaction(FileIdentifier fileId, string reaction, ReactionTransitOptions options, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.React);

        var result = new AddReactionResult();

        await reactionContentService.AddReaction(file, reaction, odinContext, connection);

        if (options?.Recipients?.Any() ?? false)
        {
            //TODO: enqueue in outbox and update result
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
        await reactionContentService.DeleteReaction(file, reaction, odinContext, connection);

        if (options?.Recipients?.Any() ?? false)
        {
            //TODO: enqueue in outbox and update result
        }

        return result;
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(FileIdentifier fileId, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionCountsByFile(file, odinContext, connection);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId identity, FileIdentifier fileId, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertIsValidOdinId(identity, out _);

        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionsByIdentityAndFile(identity, file, odinContext, connection);
    }

    public async Task<GetReactionsResponse> GetReactions(FileIdentifier fileId, int cursor, int maxCount, IOdinContext odinContext,
        DatabaseConnection connection, FileSystemType fileSystemType)
    {
        var file = await GetLocalFileId(fileId, odinContext, connection, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);
        
        return await reactionContentService.GetReactions(file, cursor, maxCount, odinContext, connection);
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