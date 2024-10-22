using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions.Group;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;

namespace Odin.Services.Drives.Reactions.Redux.Group;

public class GroupReactionService(
    TenantContext tenantContext,
    ReactionContentService reactionContentService,
    PeerOutbox peerOutbox,
    PeerOutboxProcessorBackgroundService outboxProcessorBackgroundService,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    FileSystemResolver fileSystemResolver) : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
{
    private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;

    public async Task<AddReactionResult> AddReaction(FileIdentifier fileId, string reaction, ReactionTransitOptions options, IOdinContext odinContext,
        IdentityDatabase db, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var localFile = await GetLocalFileId(fileId, odinContext, db, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(localFile.DriveId, DrivePermission.React);

        var result = new AddReactionResult();

        await reactionContentService.AddReactionAsync(localFile, reaction, odinContext.GetCallerOdinIdOrFail(), odinContext, db);

        if (options?.Recipients?.Any() ?? false)
        {
            foreach (var recipient in options.Recipients)
            {
                var status = await EnqueueRemoteReactionOutboxItem(OutboxItemType.AddRemoteReaction, (OdinId)recipient, fileId, reaction, localFile,
                    odinContext, db, fileSystemType);
                result.RecipientStatus.Add(recipient, status);
            }

            outboxProcessorBackgroundService.PulseBackgroundProcessor();
        }

        return result;
    }

    public async Task<DeleteReactionResult> DeleteReaction(FileIdentifier fileId, string reaction, ReactionTransitOptions options, IOdinContext odinContext,
        IdentityDatabase db, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var localFile = await GetLocalFileId(fileId, odinContext, db, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(localFile.DriveId, DrivePermission.React);

        var result = new DeleteReactionResult();
        await reactionContentService.DeleteReactionAsync(localFile, reaction, odinContext.GetCallerOdinIdOrFail(), odinContext, db);

        if (options?.Recipients?.Any() ?? false)
        {
            foreach (var recipient in options.Recipients)
            {
                var status = await EnqueueRemoteReactionOutboxItem(OutboxItemType.DeleteRemoteReaction, (OdinId)recipient, fileId, reaction, localFile,
                    odinContext, db, fileSystemType);
                result.RecipientStatus.Add(recipient, status);
            }

            outboxProcessorBackgroundService.PulseBackgroundProcessor();
        }

        return result;
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(FileIdentifier fileId, IOdinContext odinContext,
        IdentityDatabase db, FileSystemType fileSystemType)
    {
        var file = await GetLocalFileId(fileId, odinContext, db, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionCountsByFileAsync(file, odinContext, db);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFile(OdinId identity, FileIdentifier fileId, IOdinContext odinContext,
        IdentityDatabase db, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertIsValidOdinId(identity, out _);

        var file = await GetLocalFileId(fileId, odinContext, db, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionsByIdentityAndFileAsync(identity, file, odinContext, db);
    }

    public async Task<GetReactionsResponse> GetReactions(FileIdentifier fileId, int cursor, int maxCount, IOdinContext odinContext,
        IdentityDatabase db, FileSystemType fileSystemType)
    {
        var file = await GetLocalFileId(fileId, odinContext, db, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionsAsync(file, cursor, maxCount, odinContext, db);
    }

    //

    private async Task<InternalDriveFileId> GetLocalFileId(FileIdentifier fileId, IOdinContext odinContext, IdentityDatabase db,
        FileSystemType fileSystemType)
    {
        var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);
        var localFileId = (await fs.Query.ResolveFileId(fileId.ToGlobalTransitIdFileIdentifier(), odinContext, db)).GetValueOrDefault();

        if (!localFileId.IsValid())
        {
            throw new OdinClientException("No local file found by the global transit id", OdinClientErrorCode.InvalidFile);
        }

        return localFileId;
    }

    private async Task<TransferStatus> EnqueueRemoteReactionOutboxItem(OutboxItemType outboxItemType,
        OdinId recipient,
        FileIdentifier file,
        string reaction,
        InternalDriveFileId localFile,
        IOdinContext odinContext,
        IdentityDatabase db,
        FileSystemType fileSystemType)
    {
        var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, db, false);
        if (null == clientAuthToken)
        {
            return TransferStatus.EnqueuedFailed;
        }

        var request = new RemoteReactionRequestRedux()
        {
            File = file,
            Payload = CreateSharedSecretEncryptedPayload(clientAuthToken, reaction),
            FileSystemType = fileSystemType
        };

        var outboxItem = new OutboxFileItem
        {
            Recipient = recipient,
            File = new InternalDriveFileId()
            {
                FileId = ByteArrayUtil.ReduceSHA256Hash(reaction),
                DriveId = localFile.DriveId
            },
            Priority = 100,
            Type = outboxItemType,
            DependencyFileId = localFile.FileId,
            State = new OutboxItemState
            {
                EncryptedClientAuthToken = clientAuthToken.ToPortableBytes(),
                Data = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray()
            }
        };

        await peerOutbox.AddItemAsync(outboxItem, db, useUpsert: true);
        return TransferStatus.Enqueued;
    }
}