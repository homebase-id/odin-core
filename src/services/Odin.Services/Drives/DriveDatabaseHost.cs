using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
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
        INotificationHandler<DriveFileDeletedNotification>,
        INotificationHandler<ReactionPreviewUpdatedNotification>
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
        public async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId, IdentityDatabase db)
        {
            //  AsyncLazy: https://devblogs.microsoft.com/pfxteam/asynclazyt/
            var manager = _queryManagers.GetOrAdd(driveId, id => new AsyncLazy<IDriveDatabaseManager>(async () =>
            {
                var drive = await _driveManager.GetDrive(id, db, failIfInvalid: true);
                var logger = _loggerFactory.CreateLogger<IDriveDatabaseManager>();

                var manager = new SqliteDatabaseManager(_tenantSystemStorage, drive, logger);
                await manager.LoadLatestIndex(db);

                return manager;
            }));

            return await manager.Value;
        }

        public async Task Handle(DriveFileChangedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId, notification.db);
            await manager.UpdateCurrentIndex(notification.ServerFileHeader, notification.db);
        }

        public async Task Handle(ReactionPreviewUpdatedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId, notification.db);
            await manager.UpdateCurrentIndex(notification.ServerFileHeader, notification.db);
        }
        
        public async Task Handle(DriveFileDeletedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId, notification.db);

            if (notification.IsHardDelete)
            {
                await manager.HardDeleteFromIndex(notification.File, notification.db);
            }
            else
            {
                await manager.SoftDeleteFromIndex(notification.ServerFileHeader, notification.db);
            }
        }

        public async Task Handle(DriveFileAddedNotification notification, CancellationToken cancellationToken)
        {
            var manager = await TryGetOrLoadQueryManager(notification.File.DriveId, notification.db);
            await manager.UpdateCurrentIndex(notification.ServerFileHeader, notification.db);
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