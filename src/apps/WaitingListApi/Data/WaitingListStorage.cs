using Odin.Core.Storage.SQLite;
using Odin.Core.Util;
using Serilog;
using WaitingListApi.Config;
using WaitingListApi.Controllers;
using WaitingListApi.Data.Database;

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

        Log.Information($"Creating database at path {dbPath}");

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new WaitingListDatabase(finalPath);
        _db.CreateDatabaseAsync(false).Wait(); // SEB:NOTE Can't be bothered. This is a temporary class.
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