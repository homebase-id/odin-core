using System;
using System.IO;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Configuration;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Core.Util;

namespace Odin.Core.Services.Base;

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
        _db = new ServerDatabase($"Data Source={finalPath}");
        _db.CreateDatabase(false);

        //temp test
        JobQueue = _db.tblCron;
    }

    public DatabaseBase.LogicCommitUnit CreateCommitUnitOfWork()
    {
        return _db.CreateCommitUnitOfWork();
    }

    public void EnqueueJob(OdinId odinId, CronJobType jobType, byte[] data)
    {
        try
        {
            this.JobQueue.Insert(new CronRecord()
            {
                identityId = odinId,
                type = (Int32)jobType,
                data = data
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

    public void EnqueueJob<T>(OdinId odinId, CronJobType jobType, T data)
    {
        this.JobQueue.Insert(new CronRecord()
        {
            identityId = odinId,
            type = (Int32)jobType,
            data = OdinSystemSerializer.Serialize(data).ToUtf8ByteArray()
        });
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}