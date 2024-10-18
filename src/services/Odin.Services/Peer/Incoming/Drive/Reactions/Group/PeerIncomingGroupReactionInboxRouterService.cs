using System;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
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
    FileSystemResolver fileSystemResolver)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver)
{
    public async Task<PeerResponseCode> AddReaction(RemoteReactionRequestRedux request, IOdinContext odinContext, IdentityDatabase db)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Payload, nameof(request.Payload));
        OdinValidationUtils.AssertNotNull(request.File, nameof(request.File));
        request.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

        odinContext.PermissionsContext.AssertHasDrivePermission(request.File.TargetDrive, DrivePermission.React);

        await RouteReactionActionToInbox(TransferInstructionType.AddReaction, request, odinContext, db);
        return PeerResponseCode.AcceptedIntoInbox;
    }

    public async Task<PeerResponseCode> DeleteReaction(RemoteReactionRequestRedux request, IOdinContext odinContext, IdentityDatabase db)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Payload, nameof(request.Payload));
        OdinValidationUtils.AssertNotNull(request.File, nameof(request.File));
        request.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

        odinContext.PermissionsContext.AssertHasDrivePermission(request.File.TargetDrive, DrivePermission.React);

        await RouteReactionActionToInbox(TransferInstructionType.DeleteReaction, request, odinContext, db);
        return PeerResponseCode.AcceptedIntoInbox;
    }

    private async Task RouteReactionActionToInbox(TransferInstructionType instruction, RemoteReactionRequestRedux request, IOdinContext odinContext,
        IdentityDatabase db)
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
            DriveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive),
            TransferFileType = TransferFileType.Normal,
            GlobalTransitId = file.GlobalTransitId.GetValueOrDefault(),
            FileSystemType = request.FileSystemType,

            Data = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray()
        };

        await transitInboxBoxStorage.Add(item);

        await mediator.Publish(new InboxItemReceivedNotification()
        {
            TargetDrive = file.TargetDrive,
            TransferFileType = TransferFileType.Normal,
            FileSystemType = item.FileSystemType,
            OdinContext = odinContext,
            db = db
        });
    }
}