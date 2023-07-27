using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;

namespace Odin.Core.Services.Drives
{
    /// <summary>
    /// Hosts all the instances of the DriveDatabase across all drives
    /// </summary>
    public class DriveDatabaseHost : IDisposable,
        INotificationHandler<DriveFileAddedNotification>,
        INotificationHandler<DriveFileChangedNotification>,
        INotificationHandler<DriveFileDeletedNotification>
    {
        private readonly DriveManager _driveManager;
        private readonly Dictionary<Guid, IDriveDatabaseManager> _queryManagers;
        private readonly ILoggerFactory _loggerFactory;

        private readonly SemaphoreSlim _mutex = new (1, 1);

        public DriveDatabaseHost(ILoggerFactory loggerFactory, DriveManager driveManager)
        {
            _loggerFactory = loggerFactory;
            _driveManager = driveManager;
            _queryManagers = new Dictionary<Guid, IDriveDatabaseManager>();
        }

        public async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId)
        {
            await _mutex.WaitAsync();
            try
            {
                if (_queryManagers.TryGetValue(driveId, out var manager))
                {
                    return manager;
                }

                var drive = await _driveManager.GetDrive(driveId, failIfInvalid: true);
                var logger = _loggerFactory.CreateLogger<IDriveDatabaseManager>();

                manager = new SqliteDatabaseManager(drive, logger);
                _queryManagers.TryAdd(drive.Id, manager);
                await manager.LoadLatestIndex();

                return manager;
            }
            finally
            {
                _mutex.Release();
            }
        }

        public async Task Handle(DriveFileChangedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId);
            await manager.UpdateCurrentIndex(notification.ServerFileHeader);
        }

        public async Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId);

            if (notification.IsHardDelete)
            {
                 await manager.RemoveFromCurrentIndex(notification.File);
            }

            await manager.UpdateCurrentIndex(notification.ServerFileHeader);
        }

        public async Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId);
            await manager.UpdateCurrentIndex(notification.ServerFileHeader);
        }

        public void Dispose()
        {
            foreach (var manager in _queryManagers.Values)
            {
                manager.Dispose();
            }
        }
        
    }
}