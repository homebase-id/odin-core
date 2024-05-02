using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.Apps.CommandMessaging;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base;

public abstract class DriveCommandServiceBase : RequirePermissionsBase
{
    private readonly DriveDatabaseHost _driveDatabaseHost;
    private readonly DriveStorageServiceBase _storage;

    protected DriveCommandServiceBase(DriveDatabaseHost driveDatabaseHost, DriveStorageServiceBase storage, DriveManager driveManager)
    {
        _driveDatabaseHost = driveDatabaseHost;
        _storage = storage;
        DriveManager = driveManager;
    }

    protected override DriveManager DriveManager { get; }

    public async Task EnqueueCommandMessage(Guid driveId, List<Guid> fileIds, DatabaseConnection cn)
    {
        var manager = await TryGetOrLoadQueryManager(driveId, cn);
        await manager.AddCommandMessage(fileIds, cn);
    }

    public async Task<List<ReceivedCommand>> GetUnprocessedCommands(Guid driveId, int count, IOdinContext odinContext, DatabaseConnection cn)
    {
        var manager = await TryGetOrLoadQueryManager(driveId, cn);
        var unprocessedCommands = await manager.GetUnprocessedCommands(count, cn);

        var result = new List<ReceivedCommand>();

        foreach (var cmd in unprocessedCommands)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = cmd.Id
            };

            var serverFileHeader = await _storage.GetServerFileHeader(file, odinContext, cn);
            if (null == serverFileHeader)
            {
                continue;
            }

            var commandFileHeader = DriveFileUtility.CreateClientFileHeader(serverFileHeader, odinContext);
            var command = OdinSystemSerializer.Deserialize<CommandTransferMessage>(commandFileHeader.FileMetadata.AppData.Content);

            result.Add(new ReceivedCommand()
            {
                Id = commandFileHeader.FileId, //TODO: should this be the ID?
                Sender = commandFileHeader.FileMetadata.SenderOdinId,
                ClientCode = commandFileHeader.FileMetadata.AppData.DataType,
                ClientJsonMessage = command.ClientJsonMessage,
                GlobalTransitIdList = command!.GlobalTransitIdList
            });
        }

        return result;
    }

    public async Task MarkCommandsProcessed(Guid driveId, List<Guid> idList, DatabaseConnection cn)
    {
        var manager = await TryGetOrLoadQueryManager(driveId, cn);
        await manager.MarkCommandsCompleted(idList, cn);
    }

    private async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId, DatabaseConnection cn, bool onlyReadyManagers = true)
    {
        return await _driveDatabaseHost.TryGetOrLoadQueryManager(driveId, cn);
    }
}