using System;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.Factory.Sqlite;

public sealed class SqlitePoolBoy : IDisposable
{
    public static void ClearAllPools()
    {
        SqliteConnection.ClearAllPools();
    }

    public static void ClearPool(string connectionString)
    {
        using var cn = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(cn);
    }
    
    public void Dispose()
    {
        ClearAllPools();
    }
}