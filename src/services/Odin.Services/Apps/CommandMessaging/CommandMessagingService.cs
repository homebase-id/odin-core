using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Apps.CommandMessaging;

/// <summary>
/// Enables apps to send commands to other identities for notifications
/// </summary>
/// <remarks>
/// Uses transit to send commands as special files
/// </remarks>
public class CommandMessagingService
{
    private readonly IPeerOutgoingTransferService _peerOutgoingTransferService;
    private readonly StandardFileSystem _standardFileSystem;

    public CommandMessagingService(IPeerOutgoingTransferService peerOutgoingTransferService, StandardFileSystem standardFileSystem)
    {
        _peerOutgoingTransferService = peerOutgoingTransferService;
        _standardFileSystem = standardFileSystem;
    }

    public async Task<CommandMessageResult> SendCommandMessage(Guid driveId, CommandMessage command)
    {
        var internalFile = await _standardFileSystem.Storage.CreateInternalFileId(driveId);

        var msg = new CommandTransferMessage()
        {
            ClientJsonMessage = command.JsonMessage,
            GlobalTransitIdList = command.GlobalTransitIdList
        };

        var keyHeader = KeyHeader.NewRandom16();
        var fileMetadata = new FileMetadata(internalFile)
        {
            GlobalTransitId = null,
            Created = UnixTimeUtc.Now().milliseconds,
            IsEncrypted = true,
            AppData = new AppFileMetaData()
            {
                FileType = ReservedFileTypes.CommandMessage,
                Content = OdinSystemSerializer.Serialize(msg),
                DataType = command.Code
            }
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.Connected,
            AllowDistribution = true,
            DoNotIndex = true
        };

        var serverFileHeader = await _standardFileSystem.Storage.CreateServerFileHeader(internalFile, keyHeader, fileMetadata, serverMetadata);
        await _standardFileSystem.Storage.WriteNewFileHeader(internalFile, serverFileHeader);

        //TODO: with the introduction of file system type, we can probably make commands a file system type
        var transferResult = await _peerOutgoingTransferService.SendFile(
            internalFile: internalFile,
            options: new TransitOptions()
            {
                IsTransient = true,
                Recipients = command.Recipients,
                UseGlobalTransitId = false,
                Schedule = ScheduleOptions.SendNowAwaitResponse //TODO: let the caller specify this
            },
            transferFileType: TransferFileType.CommandMessage,
            FileSystemType.Standard);

        return new CommandMessageResult()
        {
            RecipientStatus = transferResult
        };
    }

    /// <summary>
    /// Gets a list of commands ready to be processed along with their associated files
    /// </summary>
    /// <returns></returns>
    public async Task<ReceivedCommandResultSet> GetUnprocessedCommands(Guid driveId, string cursor)
    {
        var commands = await _standardFileSystem.Commands.GetUnprocessedCommands(driveId, count: 100);
        return new ReceivedCommandResultSet()
        {
            ReceivedCommands = commands
        };
    }

    public async Task MarkCommandsProcessed(Guid driveId, List<Guid> commandIdList)
    {
        var list = new List<InternalDriveFileId>();

        foreach (var commandId in commandIdList)
        {
            list.Add(new InternalDriveFileId()
            {
                FileId = commandId,
                DriveId = driveId
            });
        }

        foreach (var internalDriveFileId in list)
        {
            await _standardFileSystem.Storage.HardDeleteLongTermFile(internalDriveFileId);
        }

        await _standardFileSystem.Commands.MarkCommandsProcessed(driveId, commandIdList.ToList());
    }
}