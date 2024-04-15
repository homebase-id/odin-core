using System;
using System.IO;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Configuration;
using static Odin.Core.Storage.SQLite.DatabaseBase;

namespace Odin.Services.Base;

/// <summary>
/// Stores system-wide data
/// </summary>
public class ServerSystemStorage : IDisposable
{
    private readonly ServerDatabase _db;
    public readonly TableCron JobQueue;

    public ServerSystemStorage(OdinConfiguration config)
    {
        string dbPath = config.Host.SystemDataRootPath;
        string dbName = "sys.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new ServerDatabase(finalPath);
        _db.CreateDatabase(false);

        //temp test
        JobQueue = _db.tblCron;
    }

    public void EnqueueJob(OdinId odinId, CronJobType jobType, byte[] data, UnixTimeUtc nextRun)
    {
        try
        {
            this.JobQueue.Insert(new CronRecord()
            {
                identityId = odinId,
                type = (Int32)jobType,
                data = data,
                nextRun = nextRun
            });
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            //ignore constraint error code as it just means we tried to insert the sender twice.
            //it's only needed once
            if (ex.ErrorCode != 19) //constraint
            {
                throw;
            }
        }
    }

    public void EnqueueJob<T>(OdinId odinId, CronJobType jobType, T data, UnixTimeUtc nextRun)
    {
        try
        {
            this.JobQueue.Insert(new CronRecord()
            {
                identityId = odinId,
                type = (Int32)jobType,
                data = OdinSystemSerializer.Serialize(data).ToUtf8ByteArray(),
                nextRun = nextRun
            });
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            //ignore constraint error code as it just means we tried to insert the sender twice.
            //it's only needed once
            if (ex.ErrorCode != 19) //constraint
            {
                throw;
            }
        }
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}