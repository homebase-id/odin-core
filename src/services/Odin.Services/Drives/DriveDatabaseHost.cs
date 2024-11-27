using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using LazyCache;
using Microsoft.Extensions.Logging;
using Odin.Core.Util;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives
{
    /// <summary>
    /// Hosts all the instances of the DriveDatabase across all drives
    /// </summary>
    // SEB:TODO simplify this class
    public class DriveDatabaseHost(
        SharedConcurrentDictionary<DriveDatabaseHost, Guid, AsyncLazy<IDriveDatabaseManager>> queryManagers,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        DriveManager driveManager)
    {
        // SEB:NOTE if this blows up, revert to commit 5a92a50c4d9a5dbe0790a1a15df9c20b6dc1192a
        public async Task<IDriveDatabaseManager> TryGetOrLoadQueryManagerAsync(Guid driveId)
        {
            //  AsyncLazy: https://devblogs.microsoft.com/pfxteam/asynclazyt/
            var manager = queryManagers.GetOrAdd(driveId, id => new AsyncLazy<IDriveDatabaseManager>(async () =>
            {
                var drive = await driveManager.GetDriveAsync(id, failIfInvalid: true);
                var logger = loggerFactory.CreateLogger<IDriveDatabaseManager>();

                var manager = new SqliteDatabaseManager(serviceProvider, drive, logger);
                await manager.LoadLatestIndexAsync(); // SEB:NOTE this does nothing

                return manager;
            }));

            return await manager.Value;
        }

    }
}