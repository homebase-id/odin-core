using System;
using System.IO;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Base;

/// <summary>
/// Stores system-wide data
/// </summary>
public sealed class ServerSystemStorage : IDisposable
{
    private readonly ServerDatabase _db;

    public ServerSystemStorage(OdinConfiguration config)
    {
        string dbPath = config.Host.SystemDataRootPath;
        string dbName = "sys.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        var finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new ServerDatabase(finalPath);
        using var cn = _db.CreateDisposableConnection();
        _db.CreateDatabaseAsync(false).Wait(); // SEB:TODOMove out of ctor and make async
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    public DatabaseConnection CreateConnection()
    {
        return _db.CreateDisposableConnection();
    }

    public TableJobs Jobs => _db.tblJobs;
}
