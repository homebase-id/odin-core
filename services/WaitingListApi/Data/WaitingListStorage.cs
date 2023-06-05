using WaitingListApi.Config;
using WaitingListApi.Controllers;
using WaitingListApi.Data.Database;
using Youverse.Core.Storage.Sqlite;
using Youverse.Core.Util;

namespace WaitingListApi.Data;

/// <summary>
/// Stores system-wide data
/// </summary>
public class WaitingListStorage : IDisposable
{
    private readonly WaitingListDatabase _db;

    public WaitingListStorage(WaitingListConfig config)
    {
        string dbPath = config.Host.SystemDataRootPath;
        
        string dbName = "waitinglist.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new WaitingListDatabase($"Data Source={finalPath}");
        _db.CreateDatabase(false);

    }

    public DatabaseBase.LogicCommitUnit CreateCommitUnitOfWork()
    {
        return _db.CreateCommitUnitOfWork();
    }

    public void Insert(NotificationInfo info)
    {
        this._db.WaitingListTable?.Insert(new WaitingListRecord()
        {
            EmailAddress = info.EmailAddress,
            JsonData = info.Data
        });
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}