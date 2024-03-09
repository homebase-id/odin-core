using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.Apps.CommandMessaging;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives.FileSystem.Base;

public abstract class DriveCommandServiceBase : RequirePermissionsBase
{
    private readonly DriveDatabaseHost _driveDatabaseHost;
    private readonly DriveStorageServiceBase _storage;

    protected DriveCommandServiceBase(DriveDatabaseHost driveDatabaseHost, DriveStorageServiceBase storage, OdinContextAccessor contextAccessor, DriveManager driveManager)
    {
        _driveDatabaseHost = driveDatabaseHost;
        _storage = storage;
        ContextAccessor = contextAccessor;
        DriveManager = driveManager;
    }

    protected override DriveManager DriveManager { get; }
    protected override OdinContextAccessor ContextAccessor { get; }

    public async Task EnqueueCommandMessage(Guid driveId, List<Guid> fileIds)
    {
        var manager = await TryGetOrLoadQueryManager(driveId);
        await manager.AddCommandMessage(fileIds);
    }

    public async Task<List<ReceivedCommand>> GetUnprocessedCommands(Guid driveId, int count)
    {
        var manager = await TryGetOrLoadQueryManager(driveId);
        var unprocessedCommands = await manager.GetUnprocessedCommands(count);

        var result = new List<ReceivedCommand>();

        foreach (var cmd in unprocessedCommands)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = cmd.Id
            };

            var serverFileHeader = await _storage.GetServerFileHeader(file);
            if (null == serverFileHeader)
            {
                continue;
            }
            var commandFileHeader = DriveFileUtility.ConvertToSharedSecretEncryptedClientFileHeader(serverFileHeader, ContextAccessor);
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

    public async Task MarkCommandsProcessed(Guid driveId, List<Guid> idList)
    {
        var manager = await TryGetOrLoadQueryManager(driveId);
        await manager.MarkCommandsCompleted(idList);
    }

    private async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId, bool onlyReadyManagers = true)
    {
        return await _driveDatabaseHost.TryGetOrLoadQueryManager(driveId);
    }
}