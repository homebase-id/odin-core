using System;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;

namespace Odin.Services.Peer.Incoming.Drive.Reactions.Group;

/// <summary>
/// Handles incoming reactions and routes to the inbox
/// </summary>
public class PeerIncomingGroupReactionInboxRouterService(
    TransitInboxBoxStorage transitInboxBoxStorage,
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    IMediator mediator,
    FileSystemResolver fileSystemResolver,
    OdinConfiguration odinConfiguration,
    IDriveManager driveManager,
    ReactionContentService reactionContentService)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
{
    public async Task<PeerResponseCode> AddReaction(RemoteReactionRequestRedux request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Payload, nameof(request.Payload));
        OdinValidationUtils.AssertNotNull(request.File, nameof(request.File));
        request.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

        if (await TryApplyToZeroKnowledgeDriveAsync(TransferInstructionType.AddReaction, request, odinContext))
        {
            return PeerResponseCode.AcceptedDirectWrite;
        }

        odinContext.PermissionsContext.AssertHasDrivePermission(request.File.TargetDrive.Alias, DrivePermission.React);

        await RouteReactionActionToInboxAsync(TransferInstructionType.AddReaction, request, odinContext);
        return PeerResponseCode.AcceptedIntoInbox;
    }

    public async Task<PeerResponseCode> DeleteReaction(RemoteReactionRequestRedux request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Payload, nameof(request.Payload));
        OdinValidationUtils.AssertNotNull(request.File, nameof(request.File));
        request.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

        if (await TryApplyToZeroKnowledgeDriveAsync(TransferInstructionType.DeleteReaction, request, odinContext))
        {
            return PeerResponseCode.AcceptedDirectWrite;
        }

        odinContext.PermissionsContext.AssertHasDrivePermission(request.File.TargetDrive.Alias, DrivePermission.React);

        await RouteReactionActionToInboxAsync(TransferInstructionType.DeleteReaction, request, odinContext);
        return PeerResponseCode.AcceptedIntoInbox;
    }

    /// <summary>
    /// No inbox on a zero-knowledge drive: a peer's reaction is applied at once, as the peer, by drive <c>React</c>, and on
    /// Private Share by a share's <c>React</c> bit in place of the drive's (docs/selective-share-pr3-plan.md, sections 7
    /// and 10).  The file is found without the read rule, which hides a client-locked file from a peer; reacting reads
    /// nothing of it.  False for a Server Share drive.
    /// </summary>
    private async Task<bool> TryApplyToZeroKnowledgeDriveAsync(TransferInstructionType instruction, RemoteReactionRequestRedux request,
        IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(request.File.TargetDrive.Alias);
        if (drive == null || drive.HasInbox)
        {
            return false;
        }

        // The file reacted to, a comment as much as a standard file, by drive React alone
        var file = (await GetHeaderForPeerWriteAsync(FileSystemResolver.ResolveFileSystem(request.FileSystemType),
            request.File.ToGlobalTransitIdFileIdentifier(), odinContext, DrivePermission.React)).FileMetadata.File;
        var reaction = await DecryptUsingSharedSecretAsync<string>(request.Payload, odinContext);
        var sender = odinContext.GetCallerOdinIdOrFail();
        _ = instruction == TransferInstructionType.AddReaction
            ? await reactionContentService.AddReactionAsync(file, reaction, sender, odinContext, null)
            : await reactionContentService.DeleteReactionAsync(file, reaction, sender, odinContext, null);
        return true;
    }

    private async Task RouteReactionActionToInboxAsync(TransferInstructionType instruction, RemoteReactionRequestRedux request,
        IOdinContext odinContext)
    {
        var file = request.File;

        var item = new TransferInboxItem()
        {
            Id = Guid.NewGuid(),
            AddedTimestamp = UnixTimeUtc.Now(),
            Sender = odinContext.GetCallerOdinIdOrFail(),
            InstructionType = instruction,

            //HACK: use random guid for the fileId UID constraint since we can have multiple
            //senders sending an add/delete reaction for the same gtid
            FileId = Guid.NewGuid(),
            DriveId = file.TargetDrive.Alias,
            TransferFileType = TransferFileType.Normal,
            GlobalTransitId = file.GlobalTransitId.GetValueOrDefault(),
            FileSystemType = request.FileSystemType,

            Data = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray()
        };

        await transitInboxBoxStorage.AddAsync(item);

        await mediator.Publish(new InboxItemReceivedNotification()
        {
            TargetDrive = file.TargetDrive,
            TransferFileType = TransferFileType.Normal,
            FileSystemType = item.FileSystemType,
        });
    }
}