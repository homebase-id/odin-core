using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
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
        private readonly ConcurrentDictionary<Guid, IDriveDatabaseManager> _queryManagers;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IAppCache _queryManagerCache;

        public DriveDatabaseHost(ILoggerFactory loggerFactory, DriveManager driveManager)
        {
            _loggerFactory = loggerFactory;
            _driveManager = driveManager;
            _queryManagers = new ConcurrentDictionary<Guid, IDriveDatabaseManager>();

            _queryManagerCache = new CachingService();
            // InitializeQueryManagers();
        }

        public bool TryGetOrLoadQueryManager(Guid driveId, out IDriveDatabaseManager manager)
        {
            var creator = new Func<Task<IDriveDatabaseManager>>(delegate
            {
                var logger = _loggerFactory.CreateLogger<IDriveDatabaseManager>();
                var drive = _driveManager.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();

                var m = new SqliteDatabaseManager(drive, logger);
                m.LoadLatestIndex().GetAwaiter().GetResult();
                return Task.FromResult<IDriveDatabaseManager>(m);
            });

            manager = _queryManagerCache.GetOrAdd(driveId.ToString(), creator).GetAwaiter().GetResult();

            //TODO: probably not needed anymore
            return manager != null;

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

        // private async void InitializeQueryManagers()
        // {
        //     var allDrives = await _driveManager.GetDrives(new PageOptions(1, Int32.MaxValue));
        //     foreach (var drive in allDrives.Results)
        //     {
        //         await this.LoadQueryManager(drive, out var _);
        //     }
        // }

        // private Task LoadQueryManager(StorageDrive drive, out IDriveDatabaseManager manager)
        // {
        //     var logger = _loggerFactory.CreateLogger<IDriveDatabaseManager>();
        //
        //     manager = new SqliteDatabaseManager(drive, logger);
        //     _queryManagers.TryAdd(drive.Id, manager);
        //     manager.LoadLatestIndex().GetAwaiter().GetResult();
        //     return Task.CompletedTask;
        // }
    }
}