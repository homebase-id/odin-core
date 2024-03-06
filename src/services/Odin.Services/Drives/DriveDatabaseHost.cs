using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;

namespace Odin.Services.Drives
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
        private readonly ConcurrentDictionary<Guid, AsyncLazy<IDriveDatabaseManager>> _queryManagers = new();
        private readonly ILoggerFactory _loggerFactory;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public DriveDatabaseHost(ILoggerFactory loggerFactory, DriveManager driveManager, TenantSystemStorage tenantSystemStorage)
        {
            _loggerFactory = loggerFactory;
            _driveManager = driveManager;
            _tenantSystemStorage = tenantSystemStorage;
        }

        // SEB:NOTE if this blows up, revert to commit 5a92a50c4d9a5dbe0790a1a15df9c20b6dc1192a
        public async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId)
        {
            //  AsyncLazy: https://devblogs.microsoft.com/pfxteam/asynclazyt/
            var manager = _queryManagers.GetOrAdd(driveId, id => new AsyncLazy<IDriveDatabaseManager>(async () =>
            {
                var drive = await _driveManager.GetDrive(id, failIfInvalid: true);
                var logger = _loggerFactory.CreateLogger<IDriveDatabaseManager>();

                var manager = new SqliteDatabaseManager(_tenantSystemStorage, drive, logger);
                await manager.LoadLatestIndex();

                return manager;
            }));

            return await manager.Value;
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
            while (!_queryManagers.IsEmpty)
            {
                foreach (var key in _queryManagers.Keys)
                {
                    if (_queryManagers.TryRemove(key, out var manager))
                    {
                        manager.Value.GetAwaiter().GetResult().Dispose();
                        break; // POP!
                    }
                }
            }
        }
    }
}