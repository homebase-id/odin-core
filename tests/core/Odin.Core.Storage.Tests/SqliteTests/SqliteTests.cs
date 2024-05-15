using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Odin.Core.Storage.Tests.SqliteTests;

public class SqliteTests
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _mutex = new();

    public SqliteTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var builder = new SqliteConnectionStringBuilder
        {
            // Set the properties
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };
        _connectionString = builder.ToString();
    }

    [SetUp]
    public void Setup()
    {
        File.Delete(_dbPath);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE Counts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Count INTEGER NOT NULL);
                """;
            command.ExecuteNonQuery();
        }
    }

    [Test, Explicit]
    public void InsertsAreThreadSafe()
    {
        const int count = 10000;
        var threads = new List<Thread>();
        for (var i = 0; i < count; i++)
        {
            var thread = new Thread(Inserts);
            threads.Add(thread);
        }

        for (var i = 0; i < count; i++)
        {
            threads[i].Start(i);
        }

        for (var i = 0; i < count; i++)
        {
            threads[i].Join();
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var rowCount = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM Counts;";
            var result = command.ExecuteScalar();
            rowCount = Convert.ToInt32(result);
        }

        Assert.AreEqual(count, rowCount);
    }

    [Test, Explicit]
    public void PragmasAreNotThreadSafe()
    {
        const int count = 10000;
        var threads = new List<Thread>();
        for (var i = 0; i < count; i++)
        {
            var thread = new Thread(Pragmas);
            threads.Add(thread);
        }

        for (var i = 0; i < count; i++)
        {
            threads[i].Start(i);
        }

        for (var i = 0; i < count; i++)
        {
            threads[i].Join();
        }

        // We'll crash before we get here

    }

    private void Pragmas(object obj)
    {
        // lock (_mutex)
        {
            var index = (int)obj;
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var pragmaJournalModeCommand = connection.CreateCommand())
            {
                pragmaJournalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
                pragmaJournalModeCommand.CommandText = "PRAGMA synchronous=NORMAL;";
                pragmaJournalModeCommand.ExecuteNonQuery();
            }
        }
    }

    private void Inserts(object obj)
    {
        // lock (_mutex)
        {
            var index = (int)obj;
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO Counts (Count) VALUES (@count);";
                command.Parameters.AddWithValue("@count", index);
                command.ExecuteNonQuery();
            }
        }
    }

}