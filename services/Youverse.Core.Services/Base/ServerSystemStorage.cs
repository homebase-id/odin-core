using System;
using System.IO;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Storage.Sqlite;
using Youverse.Core.Storage.Sqlite.ServerDatabase;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Base;

/// <summary>
/// Stores system-wide data
/// </summary>
public class ServerSystemStorage : IDisposable
{
    private readonly ServerDatabase _db;
    public readonly TableCron tblCron;

    public ServerSystemStorage(YouverseConfiguration config)
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
        tblCron = _db.tblCron;
    }

    public DatabaseBase.LogicCommitUnit CreateCommitUnitOfWork()
    {
        return _db.CreateCommitUnitOfWork();
    }

    public void EnqueueJob(OdinId odinId, CronJobType jobType, byte[] data)
    {
        this.tblCron.Insert(new CronRecord()
        {
            identityId = odinId,
            type = (Int32)jobType,
            data = data
        });
    }
    
    public void EnqueueJob<T>(OdinId odinId, CronJobType jobType, T data)
    {
        this.tblCron.Insert(new CronRecord()
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