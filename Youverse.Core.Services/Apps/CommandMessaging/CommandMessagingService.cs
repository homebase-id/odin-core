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
    private readonly DotYouContextAccessor _contextAccessor;

    public CommandMessagingService(ITransitService transitService, IDriveService driveService, TenantContext tenantContext, IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor)
    {
        _transitService = transitService;
        _driveService = driveService;
        _tenantContext = tenantContext;
        _driveQueryService = driveQueryService;
        _contextAccessor = contextAccessor;
    }

    public async Task<CommandMessageResult> SendCommandMessage(Guid driveId, CommandMessage command)
    {
        Guard.Argument(command, nameof(command)).NotNull().Require(m => m.IsValid());

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
            Created = UnixTimeUtcMilliseconds.Now().milliseconds,
            OriginalRecipientList = null,
            PayloadIsEncrypted = true,
            AppData = new AppFileMetaData()
            {
                FileType = ReservedFileTypes.CommandMessage,
                JsonContent = DotYouSystemSerializer.Serialize(msg),
                DataType = command.Code
            }
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            DoNotIndex = true
        };

        var serverFileHeader = await _driveService.CreateServerFileHeader(internalFile, keyHeader, fileMetadata, serverMetadata);
        await _driveService.UpdateActiveFileHeader(internalFile, serverFileHeader);

        var transferResult = await _transitService.SendFile(internalFile, new TransitOptions()
        {
            IsTransient = true,
            Recipients = command.Recipients,
            UseGlobalTransitId = false
        });

        return new CommandMessageResult()
        {
            RecipientStatus = transferResult
        };
    }

    // public async Task<OutgoingCommandStatusResultSet> GetOutgoingCommandStatus(Guid driveId, string cursor)
    // {
    //     // files are not indexed
    //     // but i need a way to get the status of the command and if it has been delivered
    //     // i also need a way to determine when to delete the file
    //     // so we have to look at the outbox

    //     Outbox Key is
    //          fileId
    //          driveId
    //          recipient
    // }

    /// <summary>
    /// Gets a list of commands ready to be processed along with their associated files
    /// </summary>
    /// <returns></returns>
    public async Task<ReceivedCommandResultSet> GetUnprocessedCommands(Guid driveId, string cursor)
    {
        var targetDrive = (await _driveService.GetDrive(driveId, true)).TargetDriveInfo;
        var getCommandFilesQueryParams = FileQueryParams.FromFileType(targetDrive, ReservedFileTypes.CommandMessage);

        var receivedCommands = new List<ReceivedCommand>();
        var getCommandFileOptions = new QueryBatchResultOptions()
        {
            Cursor = cursor == string.Empty ? null : new QueryBatchCursor(cursor),
            ExcludePreviewThumbnail = true,
            IncludeJsonContent = true,
            MaxRecords = int.MaxValue
        };

        var batch = await _driveQueryService.GetBatch(driveId, getCommandFilesQueryParams, getCommandFileOptions);

        //HACK: order these oldest to newest (ascending by time) until Michael updates query engine to do this for us while supporting paging
        var orderedBatch = batch.SearchResults.OrderBy(file => file.FileId);

        foreach (var commandFileHeader in orderedBatch)
        {
            var command = DotYouSystemSerializer.Deserialize<CommandTransferMessage>(commandFileHeader.FileMetadata.AppData.JsonContent);

            receivedCommands.Add(new ReceivedCommand()
            {
                Id = commandFileHeader.FileId, //TODO: should this be the ID?
                Sender = commandFileHeader.FileMetadata.SenderDotYouId,
                ClientCode = commandFileHeader.FileMetadata.AppData.DataType,
                ClientJsonMessage = command.ClientJsonMessage,
                GlobalTransitIdList = command!.GlobalTransitIdList
            });
        }

        return new ReceivedCommandResultSet()
        {
            TargetDrive = targetDrive,
            ReceivedCommands = receivedCommands
        };
    }

    public async Task MarkCommandsProcessed(Guid driveId, IEnumerable<Guid> commandIdList)
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
            await _driveService.HardDeleteLongTermFile(internalDriveFileId);
        }
    }
}