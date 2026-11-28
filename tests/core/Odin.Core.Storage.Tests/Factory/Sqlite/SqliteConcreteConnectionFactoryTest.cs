using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Tests.Factory.Sqlite;

#nullable enable

public class SqliteConcreteConnectionFactoryTest : IocTestBase
{
    [Test]
    public async Task ItShouldConnectAndExecuteWithoutIoc()
    {
        // This demonstrates how to do normal db stuff without DI.
        // This is useful for testing the factory itself.
        // You probably shouldn't do this anywhere else.

        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(ConnectionString(NewDatabasePath()));

        await ExecuteAsync(cn, "CREATE TABLE test (name TEXT);");
        await ExecuteAsync(cn, "ALTER TABLE test ADD COLUMN age INTEGER;");

        await using var tx = await cn.BeginTransactionAsync();
        await ExecuteAsync(cn, "INSERT INTO test (name, age) VALUES ('Alice', 30);", tx);
        await tx.CommitAsync();

        // Query data
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT * FROM test;";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync()) // Ensure you call ReadAsync before accessing data
            {
                var name = reader["name"].ToString();
                Assert.That(name, Is.EqualTo("Alice"));
            }
            else
            {
                Assert.Fail("No rows were returned from the query.");
            }
        }
    }

    [Test]
    public async Task TheWalFileShrinksBackAfterAReaderHeldItWhileItGrew()
    {
        var databasePath = NewDatabasePath();
        var connectionString = ConnectionString(databasePath);
        var walPath = databasePath + "-wal";

        await using var writer = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        await ExecuteAsync(writer, "CREATE TABLE test (data BLOB);");

        // A reader in a transaction keeps the WAL from being reset, so the writes below grow it past the limit
        long peak;
        await using (var reader = (SqliteConnection)await SqliteConcreteConnectionFactory.CreateAsync(connectionString))
        {
            await using var readTx = reader.BeginTransaction(deferred: true);
            await ExecuteAsync(reader, "SELECT COUNT(*) FROM test;", readTx);

            for (var i = 0; i < 3000; i++)
            {
                await ExecuteAsync(writer, "INSERT INTO test (data) VALUES (randomblob(8000));");
            }

            peak = new FileInfo(walPath).Length;
            await readTx.CommitAsync();
        }

        // With the reader gone, the next commit checkpoints the WAL (it is far past the autocheckpoint size) and the
        // one after resets it, truncating the file to the limit
        for (var i = 0; i < 3; i++)
        {
            await ExecuteAsync(writer, "INSERT INTO test (data) VALUES (randomblob(100));");
        }

        var after = new FileInfo(walPath).Length;
        Assert.That(peak, Is.GreaterThan(SqliteConcreteConnectionFactory.JournalSizeLimitBytes),
            $"the WAL should have grown past the limit while the reader held it: peak {peak} bytes");
        Assert.That(after, Is.LessThanOrEqualTo(SqliteConcreteConnectionFactory.JournalSizeLimitBytes),
            $"the WAL stayed at {after} bytes after the reader ended (peak {peak} bytes)");
    }

    [Test]
    public async Task AWriterWaitingForTheLockGetsItSoonAfterItIsReleased()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(TempFolder, $"{Guid.NewGuid()}.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using var holder = (SqliteConnection)await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        await using var waiter = (SqliteConnection)await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
        await using (var cmd = holder.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE test (name TEXT);";
            await cmd.ExecuteNonQueryAsync();
        }

        // The holder takes the write lock and keeps it for 30 ms
        await using var holderTx = holder.BeginTransaction(deferred: false);
        var release = Task.Run(async () =>
        {
            await Task.Delay(30);
            holderTx.Commit();
        });

        // The waiter asks for the write lock while it is held. Without SQLite's busy timeout, Microsoft.Data.Sqlite sleeps
        // 150 ms before it looks again, so this would take at least 150 ms.
        var stopwatch = Stopwatch.StartNew();
        await using (var waiterTx = await Task.Run(() => waiter.BeginTransaction(deferred: false)))
        {
            stopwatch.Stop();
            waiterTx.Commit();
        }

        await release;
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(150),
            $"the waiter got the write lock {stopwatch.ElapsedMilliseconds} ms after asking; the holder released it after 30 ms");
    }

    private string NewDatabasePath() => Path.Combine(TempFolder, $"{Guid.NewGuid()}.db");

    private static string ConnectionString(string databasePath) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        Pooling = false
    }.ToString();

    private static async Task ExecuteAsync(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (transaction != null)
        {
            cmd.Transaction = transaction;
        }

        await cmd.ExecuteNonQueryAsync();
    }
}