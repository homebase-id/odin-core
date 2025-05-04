using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Extensions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Factory;

#nullable enable

public class PortabilityTests : IocTestBase
{
    [TearDown]
    public override void TearDown()
    {
        DropTestDatabaseAsync().Wait();
        base.TearDown();
    }

    private async Task CreateTestDatabaseAsync()
    {
        var scopedConnectionFactory = Services.Resolve<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (atext TEXT, along BIGINT, abool BOOLEAN);";
        await cmd.ExecuteNonQueryAsync();
    }
    
    private async Task DropTestDatabaseAsync()
    {
        var scopedConnectionFactory = Services.Resolve<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test;";
        await cmd.ExecuteNonQueryAsync();
    }
  
    //    

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldQueryDatabase(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "@atext";
        p1.Value = "test";
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "@along";
        p2.Value = 9999999999;
        p2.DbType = System.Data.DbType.Int64;
        cmd.Parameters.Add(p2);

        var p3 = cmd.CreateParameter();
        p3.ParameterName = "@abool";
        p3.Value = true;
        p3.DbType = System.Data.DbType.Boolean;
        cmd.Parameters.Add(p3);

        cmd.CommandText = "INSERT INTO test (atext, along, abool) VALUES (@atext, @along, @abool);";
        Assert.That(cmd.RenderSqlForDebugging(), Is.EqualTo("INSERT INTO test (atext, along, abool) VALUES ('test', 9999999999, True);"));

        cmd.CommandText = "SELECT * FROM test;";
        Assert.That(cmd.RenderSqlForDebugging(), Is.EqualTo("SELECT * FROM test;"));

        var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var record = new Record();

            record.AText = (string)reader["atext"];
            Assert.That(record.AText, Is.EqualTo("test"));

            record.ALong = (long)reader["along"];
            Assert.That(record.ALong, Is.EqualTo(9999999999));

            record.ABool = Convert.ToBoolean(reader["abool"]);
            Assert.That(record.ABool, Is.True);
        }
    }

    class Record
    {
        public string? AText { get; set; }
        public long? ALong { get; set; }
        public bool? ABool { get; set; }
    }

}

