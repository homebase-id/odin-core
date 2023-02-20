using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drives.DriveCore.Query;

namespace Youverse.Core.Services.Drives.Base;

public abstract class DriveCommandServiceBase : RequirePermissionsBase
{
    private readonly DriveDatabaseHost _driveDatabaseHost;
    private readonly DriveStorageServiceBase _storage;

    protected DriveCommandServiceBase(DriveDatabaseHost driveDatabaseHost, DriveStorageServiceBase storage, DotYouContextAccessor contextAccessor, DriveManager driveManager)
    {
        _driveDatabaseHost = driveDatabaseHost;
        _storage = storage;
        ContextAccessor = contextAccessor;
        DriveManager = driveManager;
    }

    protected override DriveManager DriveManager { get; }
    protected override DotYouContextAccessor ContextAccessor { get; }

    public Task EnqueueCommandMessage(Guid driveId, List<Guid> fileIds)
    {
        TryGetOrLoadQueryManager(driveId, out var manager);
        return manager.AddCommandMessage(fileIds);
    }

    public async Task<List<ReceivedCommand>> GetUnprocessedCommands(Guid driveId, int count)
    {
        TryGetOrLoadQueryManager(driveId, out var manager);
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
            var commandFileHeader = Utility.ConvertToSharedSecretEncryptedClientFileHeader(serverFileHeader, ContextAccessor);
            var command = DotYouSystemSerializer.Deserialize<CommandTransferMessage>(commandFileHeader.FileMetadata.AppData.JsonContent);

            result.Add(new ReceivedCommand()
            {
                Id = commandFileHeader.FileId, //TODO: should this be the ID?
                Sender = commandFileHeader.FileMetadata.SenderDotYouId,
                ClientCode = commandFileHeader.FileMetadata.AppData.DataType,
                ClientJsonMessage = command.ClientJsonMessage,
                GlobalTransitIdList = command!.GlobalTransitIdList
            });
        }

        return result;
    }

    public async Task MarkCommandsProcessed(Guid driveId, List<Guid> idList)
    {
        TryGetOrLoadQueryManager(driveId, out var manager);
        await manager.MarkCommandsCompleted(idList);
    }

    private bool TryGetOrLoadQueryManager(Guid driveId, out IDriveDatabaseManager manager, bool onlyReadyManagers = true)
    {
        return _driveDatabaseHost.TryGetOrLoadQueryManager(driveId, out manager);
    }
}