using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using SQLitePCL;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Services.Apps.CommandMessaging;

/// <summary>
/// Enables apps to send commands to other identities for notifications
/// </summary>
/// <remarks>
/// Uses transit to send commands as special files
/// </remarks>
public class CommandMessagingService
{
    private readonly ITransitService _transitService;
    private readonly StandardFileSystem _standardFileSystem;

    public CommandMessagingService(ITransitService transitService, StandardFileSystem standardFileSystem)
    {
        _transitService = transitService;
        _standardFileSystem = standardFileSystem;
    }

    public async Task<CommandMessageResult> SendCommandMessage(Guid driveId, CommandMessage command)
    {
        Guard.Argument(command, nameof(command)).NotNull().Require(m => m.IsValid());

        var internalFile = _standardFileSystem.Storage.CreateInternalFileId(driveId);

        var msg = new CommandTransferMessage()
        {
            ClientJsonMessage = command.JsonMessage,
            GlobalTransitIdList = command.GlobalTransitIdList
        };

        var keyHeader = KeyHeader.NewRandom16();
        var fileMetadata = new FileMetadata(internalFile)
        {
            ContentType = "application/json",
            GlobalTransitId = null,
            Created = UnixTimeUtc.Now().milliseconds,
            OriginalRecipientList = null,
            PayloadIsEncrypted = true,
            AppData = new AppFileMetaData()
            {
                FileType = ReservedFileTypes.CommandMessage,
                JsonContent = DotYouSystemSerializer.Serialize(msg),
                DataType = command.Code,
                ContentIsComplete = true
            }
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            DoNotIndex = true
        };

        var serverFileHeader = await _standardFileSystem.Storage.CreateServerFileHeader(internalFile, keyHeader, fileMetadata, serverMetadata);
        await _standardFileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader);

        var transferResult = await _transitService.SendFile(
            internalFile: internalFile,
            options: new TransitOptions()
            {
                IsTransient = true,
                Recipients = command.Recipients,
                UseGlobalTransitId = false,
                Schedule = ScheduleOptions.SendNowAwaitResponse //TODO: let the caller specify this
            },
            transferFileType: TransferFileType.CommandMessage);

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
        Guard.Argument(commandIdList, nameof(commandIdList)).NotNull();
        
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