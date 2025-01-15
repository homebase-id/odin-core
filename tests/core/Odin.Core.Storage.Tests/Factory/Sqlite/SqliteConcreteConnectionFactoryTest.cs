using System;
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
}