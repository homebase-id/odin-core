using System;
using System.Data.Common;
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

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(TempFolder, $"{Guid.NewGuid()}.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);

        // Create table
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "CREATE TABLE test (name TEXT);";
            await cmd.ExecuteNonQueryAsync();
        }

        // Alter table
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "ALTER TABLE test ADD COLUMN age INTEGER;";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var tx = await cn.BeginTransactionAsync();

        // Insert data
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name, age) VALUES ('Alice', 30);";
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        // Query data
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT * FROM test;";
            var reader = await cmd.ExecuteReaderAsync();
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
        var databasePath = Path.Combine(TempFolder, $"{Guid.NewGuid()}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
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

        // With the reader gone, the autocheckpoint resets the WAL, and the next commit truncates it to the limit
        for (var i = 0; i < 1100; i++)
        {
            await ExecuteAsync(writer, "INSERT INTO test (data) VALUES (randomblob(100));");
        }

        var after = new FileInfo(walPath).Length;
        Assert.That(peak, Is.GreaterThan(SqliteConcreteConnectionFactory.JournalSizeLimitBytes),
            $"the WAL should have grown past the limit while the reader held it: peak {peak} bytes");
        Assert.That(after, Is.LessThanOrEqualTo(SqliteConcreteConnectionFactory.JournalSizeLimitBytes),
            $"the WAL stayed at {after} bytes after the reader ended (peak {peak} bytes)");
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        await cmd.ExecuteNonQueryAsync();
    }
}