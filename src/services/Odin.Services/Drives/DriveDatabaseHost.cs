using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using LazyCache;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives
{
    /// <summary>
    /// Hosts all the instances of the DriveDatabase across all drives
    /// </summary>
    public class DriveDatabaseHost(ILoggerFactory loggerFactory, DriveManager driveManager, TenantSystemStorage tenantSystemStorage)
        : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, AsyncLazy<IDriveDatabaseManager>> _queryManagers = new();

        // SEB:NOTE if this blows up, revert to commit 5a92a50c4d9a5dbe0790a1a15df9c20b6dc1192a
        public async Task<IDriveDatabaseManager> TryGetOrLoadQueryManager(Guid driveId, IdentityDatabase db)
        {
            //  AsyncLazy: https://devblogs.microsoft.com/pfxteam/asynclazyt/
            var manager = _queryManagers.GetOrAdd(driveId, id => new AsyncLazy<IDriveDatabaseManager>(async () =>
            {
                var drive = await driveManager.GetDrive(id, db, failIfInvalid: true);
                var logger = loggerFactory.CreateLogger<IDriveDatabaseManager>();

                var manager = new SqliteDatabaseManager(tenantSystemStorage, drive, logger);
                await manager.LoadLatestIndex(db);

                return manager;
            }));

            return await manager.Value;
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