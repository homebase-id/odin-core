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

        private readonly SemaphoreSlim _slimShady = new SemaphoreSlim(1, 1);

        public DriveDatabaseHost(ILoggerFactory loggerFactory, DriveManager driveManager)
        {
            _loggerFactory = loggerFactory;
            _driveManager = driveManager;
            _queryManagers = new Dictionary<Guid, IDriveDatabaseManager>();

            InitializeQueryManagers();
        }

        public bool TryGetOrLoadQueryManager(Guid driveId, out IDriveDatabaseManager manager)
        {

            _slimShady.WaitAsync();
            
            if (_queryManagers.TryGetValue(driveId, out manager))
            {
                return true;
            }

            var drive = _driveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            LoadQueryManager(drive, out manager);
            return true;
        }

        public Task Handle(DriveFileChangedNotification notification, CancellationToken cancellationToken)
        {
            this.TryGetOrLoadQueryManager(notification.File.DriveId, out var manager);
            return manager.UpdateCurrentIndex(notification.ServerFileHeader);
        }

        public Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken)
        {
            this.TryGetOrLoadQueryManager(notification.File.DriveId, out var manager);

            if (notification.IsHardDelete)
            {
                return manager.RemoveFromCurrentIndex(notification.File);
            }

            return manager.UpdateCurrentIndex(notification.ServerFileHeader);
        }

        public Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            this.TryGetOrLoadQueryManager(notification.File.DriveId, out var manager);
            return manager.UpdateCurrentIndex(notification.ServerFileHeader);
        }

        public void Dispose()
        {
            foreach (var manager in _queryManagers.Values)
            {
                try
                {
                    manager.Dispose();
                }
                catch
                {
                    //ignored
                }
            }
        }

        private async void InitializeQueryManagers()
        {
            var allDrives = await _driveManager.GetDrives(new PageOptions(1, Int32.MaxValue));
            foreach (var drive in allDrives.Results)
            {
                await this.LoadQueryManager(drive, out var _);
            }
        }

        private Task LoadQueryManager(StorageDrive drive, out IDriveDatabaseManager manager)
        {
            var logger = _loggerFactory.CreateLogger<IDriveDatabaseManager>();

            manager = new SqliteDatabaseManager(drive, logger);
            _queryManagers.TryAdd(drive.Id, manager);
            manager.LoadLatestIndex().GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
    }
}