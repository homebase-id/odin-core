using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Configuration;
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
    IBackgroundServiceNotifier<PeerOutboxProcessorBackgroundService> backgroundServiceNotifier,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    FileSystemResolver fileSystemResolver,
    OdinConfiguration odinConfiguration)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
{
    private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;

    public async Task<AddReactionResult> AddReactionAsync(FileIdentifier fileId, string reaction, ReactionTransitOptions options,
        IOdinContext odinContext,
        FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var localFile = await GetLocalFileIdAsync(fileId, odinContext, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(localFile.DriveId, DrivePermission.React);

        var result = new AddReactionResult();

        await reactionContentService.AddReactionAsync(localFile, reaction, odinContext.GetCallerOdinIdOrFail(), odinContext, null);

        if (options?.Recipients?.Any() ?? false)
        {
            foreach (var recipient in options.Recipients)
            {
                var status = await EnqueueRemoteReactionOutboxItemAsync(OutboxItemType.AddRemoteReaction, (OdinId)recipient, fileId,
                    reaction, localFile,
                    odinContext, fileSystemType);
                result.RecipientStatus.Add(recipient, status);
            }

            await backgroundServiceNotifier.NotifyWorkAvailableAsync();
        }

        return result;
    }

    public async Task<DeleteReactionResult> DeleteReactionAsync(FileIdentifier fileId, string reaction, ReactionTransitOptions options,
        IOdinContext odinContext, FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertValidRecipientList(options?.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
        fileId.AssertIsValid(FileIdentifierType.GlobalTransitId);

        var localFile = await GetLocalFileIdAsync(fileId, odinContext, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(localFile.DriveId, DrivePermission.React);

        var result = new DeleteReactionResult();
        await reactionContentService.DeleteReactionAsync(localFile, reaction, odinContext.GetCallerOdinIdOrFail(), odinContext, markComplete: null);

        if (options?.Recipients?.Any() ?? false)
        {
            foreach (var recipient in options.Recipients)
            {
                var status = await EnqueueRemoteReactionOutboxItemAsync(OutboxItemType.DeleteRemoteReaction, (OdinId)recipient, fileId,
                    reaction, localFile,
                    odinContext, fileSystemType);
                result.RecipientStatus.Add(recipient, status);
            }

            await backgroundServiceNotifier.NotifyWorkAvailableAsync();
        }

        return result;
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFileAsync(FileIdentifier fileId, IOdinContext odinContext,
        FileSystemType fileSystemType)
    {
        var file = await GetLocalFileIdAsync(fileId, odinContext, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionCountsByFileAsync(file, odinContext);
    }

    public async Task<List<string>> GetReactionsByIdentityAndFileAsync(OdinId identity, FileIdentifier fileId, IOdinContext odinContext,
        FileSystemType fileSystemType)
    {
        OdinValidationUtils.AssertIsValidOdinId(identity, out _);

        var file = await GetLocalFileIdAsync(fileId, odinContext, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionsByIdentityAndFileAsync(identity, file, odinContext);
    }

    public async Task<GetReactionsResponse> GetReactionsAsync(FileIdentifier fileId, int cursor, int maxCount, IOdinContext odinContext,
        FileSystemType fileSystemType)
    {
        var file = await GetLocalFileIdAsync(fileId, odinContext, fileSystemType);

        odinContext.PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        return await reactionContentService.GetReactionsAsync(file, cursor, maxCount, odinContext);
    }

    //

    private async Task<InternalDriveFileId> GetLocalFileIdAsync(FileIdentifier fileId, IOdinContext odinContext, FileSystemType fileSystemType)
    {
        var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);
        var localFileId = (await fs.Query.ResolveFileId(fileId.ToGlobalTransitIdFileIdentifier(), odinContext)).GetValueOrDefault();

        if (!localFileId.IsValid())
        {
            throw new OdinClientException("No local file found by the global transit id", OdinClientErrorCode.InvalidFile);
        }

        return localFileId;
    }

    private async Task<TransferStatus> EnqueueRemoteReactionOutboxItemAsync(OutboxItemType outboxItemType,
        OdinId recipient,
        FileIdentifier file,
        string reaction,
        InternalDriveFileId localFile,
        IOdinContext odinContext,
        FileSystemType fileSystemType)
    {
        var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);
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

        await peerOutbox.AddItemAsync(outboxItem, useUpsert: true);
        return TransferStatus.Enqueued;
    }
}