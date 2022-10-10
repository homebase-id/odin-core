using System;
using System.Collections.Generic;
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
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Storage.SQLite;

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
    private readonly IDriveService _driveService;
    private readonly IDriveQueryService _driveQueryService;
    private readonly TenantContext _tenantContext;

    public CommandMessagingService(ITransitService transitService, IDriveService driveService, TenantContext tenantContext, IDriveQueryService driveQueryService)
    {
        _transitService = transitService;
        _driveService = driveService;
        _tenantContext = tenantContext;
        _driveQueryService = driveQueryService;
    }

    public async Task<CommandMessageResult> SendCommandMessage(CommandMessage command)
    {
        Guard.Argument(command, nameof(command)).NotNull().Require(m => m.IsValid());

        var driveId = (await _driveService.GetDriveIdByAlias(command.Drive, true)).GetValueOrDefault();
        var internalFile = _driveService.CreateInternalFileId(driveId);

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
            Created = DateTimeExtensions.UnixTimeMilliseconds(),
            Updated = default,
            SenderDotYouId = default,
            PayloadSize = default,
            OriginalRecipientList = null,
            PayloadIsEncrypted = true,
            AppData = new AppFileMetaData()
            {
                Tags = default,
                FileType = ReservedFileTypes.CommandMessage,
                DataType = default,
                GroupId = default,
                UserDate = default,
                ContentIsComplete = default,
                JsonContent = DotYouSystemSerializer.Serialize(msg),
                PreviewThumbnail = default,
                AdditionalThumbnails = default,
            }
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.NewOwnerOnly,
            DoNotIndex = true
        };

        var serverFileHeader = await _driveService.CreateServerFileHeader(internalFile, keyHeader, fileMetadata, serverMetadata);
        await _driveService.WriteFileHeader(internalFile, serverFileHeader);

        var transferResult = await _transitService.SendFile(internalFile, new TransitOptions()
        {
            IsTransient = true,
            Recipients = command.Recipients,
            UseCrossReference = false
        });

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
        var targetDrive = (await _driveService.GetDrive(driveId, true)).TargetDriveInfo;
        var queryParams = FileQueryParams.FromFileType(targetDrive, ReservedFileTypes.CommandMessage);

        var receivedCommands = new List<ReceivedCommand>();
        var getCommandFileOptions = new QueryBatchResultOptions()
        {
            Cursor = cursor == string.Empty ? null : new QueryBatchCursor(cursor),
            ExcludePreviewThumbnail = true,
            IncludeJsonContent = true,
            MaxRecords = int.MaxValue
        };

        var batch = await _driveQueryService.GetBatch(driveId, queryParams, getCommandFileOptions);
        foreach (var commandFileHeader in batch.SearchResults)
        {
            var ctm = DotYouSystemSerializer.Deserialize<CommandTransferMessage>(commandFileHeader.FileMetadata.AppData.JsonContent);

            var fqp = new FileQueryParams()
            {
                TargetDrive = targetDrive,
                GlobalTransitId = ctm!.GlobalTransitIdList
            };

            var options = new QueryBatchResultOptions()
            {
                Cursor = null, //?
                ExcludePreviewThumbnail = true,
                IncludeJsonContent = true,
                MaxRecords = int.MaxValue //??
            };

            var globalTransitFileBatch = await _driveQueryService.GetBatch(driveId, fqp, options);
            receivedCommands.Add(new ReceivedCommand()
            {
                Id = commandFileHeader.FileId,
                Drive = targetDrive,
                ClientJsonMessage = ctm.ClientJsonMessage,
                MatchingFiles = globalTransitFileBatch.SearchResults,
                GlobalTransitIdList = ctm!.GlobalTransitIdList
            });
        }

        return new ReceivedCommandResultSet()
        {
            ReceivedCommands = receivedCommands
        };
    }

    public async Task MarkCommandsProcessed(IEnumerable<CommandId> commandIdList)
    {
        //commandId is the fileId of the command file

        var list = new List<InternalDriveFileId>();
        foreach (var commandId in commandIdList)
        {
            list.Add(new InternalDriveFileId()
            {
                FileId = commandId.Id,
                DriveId = (await _driveService.GetDriveIdByAlias(commandId.TargetDrive)).GetValueOrDefault()
            });
        }

        if (list.Any(f => f.DriveId == Guid.Empty))
        {
            throw new YouverseException("One or more TargetDrives are invalid in the provided commandIdList.  No Commands were marked processed");
        }

        foreach (var internalDriveFileId in list)
        {
            await _driveService.DeleteLongTermFile(internalDriveFileId);
        }
    }
}

public class CommandId
{
    public Guid Id { get; set; }
    public TargetDrive TargetDrive { get; set; }
}

public class ReceivedCommandResultSet
{
    public IEnumerable<ReceivedCommand> ReceivedCommands { get; set; }
}