using System;
using System.IO;
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

    public void Dispose()
    {
        _db.Dispose();
    }
}