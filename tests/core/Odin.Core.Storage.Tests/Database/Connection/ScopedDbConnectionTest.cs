using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Connection;
using Odin.Core.Storage.Database.Connection.System;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.Database.Connection;

public class ScopedConnectionFactoryTest
{
    private string _tempFolder;
    private ServiceProvider _services = null!;
    private LogEventMemoryStore _logEventMemoryStore = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = TempDirectory.Create();
    }

    [TearDown]
    public void TearDown()
    {
        if (_services != null)
        {
            DropTestDatabaseAsync().Wait();
            _services.Dispose();
        }
        Directory.Delete(_tempFolder, true);
        LogEvents.AssertEvents(_logEventMemoryStore.GetLogEvents());
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        SqliteConnection.ClearAllPools();
    }
    
    //

    private void RegisterServices(DatabaseType databaseType)
    {
        _logEventMemoryStore = new LogEventMemoryStore();
        
        var services = new ServiceCollection();

        if (databaseType == DatabaseType.Sqlite)
        {
            var connectionString = $"Data Source={Path.Combine(_tempFolder, "system-test.db")};Pooling=True;Cache=Shared;";
            //var connectionString = $"Data Source={Path.Combine(_tempFolder, "system-test.db")};Cache=Shared;";
            services.AddSingleton<ISystemDbConnectionFactory>(new SqliteSystemDbConnectionFactory(connectionString));
        }
        else if (databaseType == DatabaseType.PostgreSql)
        {
            var connectionString = "Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin";
            services.AddSingleton<ISystemDbConnectionFactory>(new PgsqlSystemDbConnectionFactory(connectionString));
        }

        services.AddSingleton(TestLogFactory.CreateConsoleLogger<ScopedSystemConnectionFactory>(_logEventMemoryStore));
        services.AddSingleton(TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(_logEventMemoryStore));
        services.AddScoped<ScopedSystemConnectionFactory>();
        
        _services = services.BuildServiceProvider();
    }

    private async Task CreateTestDatabaseAsync()
    {
        var scopedConnectionFactory = _services.GetRequiredService<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (name TEXT);";
        await cmd.ExecuteNonQueryAsync();
    }
    
    private async Task DropTestDatabaseAsync()
    {
        var scopedConnectionFactory = _services.GetRequiredService<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test;";
    }
  
    //    

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCreateScopedConnections(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        ScopedSystemConnectionFactory.ConnectionWrapper cn;
        await using (cn = await scopedConnectionFactory.CreateScopedConnectionAsync())
        {
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(cn.DangerousInstance, Is.Not.Null);
            await using (var cn1 = await scopedConnectionFactory.CreateScopedConnectionAsync())
            {
                Assert.That(cn.RefCount, Is.EqualTo(2));
                Assert.That(cn.DangerousInstance, Is.Not.Null);
                Assert.That(cn1.RefCount, Is.EqualTo(2));
                Assert.That(cn1.DangerousInstance, Is.SameAs(cn.DangerousInstance));
            }
            await using (var cn2 = await scopedConnectionFactory.CreateScopedConnectionAsync())
            {
                Assert.That(cn.RefCount, Is.EqualTo(2));
                Assert.That(cn.DangerousInstance, Is.Not.Null);
                Assert.That(cn2.RefCount, Is.EqualTo(2));
                Assert.That(cn2.DangerousInstance, Is.Not.Null);
                await using (var cn3 = await scopedConnectionFactory.CreateScopedConnectionAsync())
                {
                    Assert.That(cn.RefCount, Is.EqualTo(3));
                    Assert.That(cn.DangerousInstance, Is.Not.Null);
                    Assert.That(cn3.RefCount, Is.EqualTo(3));
                    Assert.That(cn3.DangerousInstance, Is.Not.Null);
                }
                Assert.That(cn.RefCount, Is.EqualTo(2));
                Assert.That(cn.DangerousInstance, Is.Not.Null);
                Assert.That(cn2.RefCount, Is.EqualTo(2));
                Assert.That(cn2.DangerousInstance, Is.Not.Null);
            }
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(cn.DangerousInstance, Is.Not.Null);
        }
        Assert.That(cn.RefCount, Is.EqualTo(0));
        Assert.That(cn.DangerousInstance, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCreateScopedTransactions(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        Assert.That(cn.RefCount, Is.EqualTo(1));
        Assert.That(cn.DangerousInstance, Is.Not.Null);

        ScopedSystemConnectionFactory.TransactionWrapper tx;
        await using (tx = await cn.BeginStackedTransactionAsync())
        {
            Assert.That(tx.RefCount, Is.EqualTo(1));
            Assert.That(tx.DangerousInstance, Is.Not.Null);
            await using (var tx1 = await cn.BeginStackedTransactionAsync())
            {
                Assert.That(tx.RefCount, Is.EqualTo(2));
                Assert.That(tx.DangerousInstance, Is.Not.Null);
                Assert.That(tx1.RefCount, Is.EqualTo(2));
                Assert.That(tx1.DangerousInstance, Is.SameAs(tx.DangerousInstance));
            }
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                Assert.That(tx.RefCount, Is.EqualTo(2));
                Assert.That(tx.DangerousInstance, Is.Not.Null);
                Assert.That(tx2.RefCount, Is.EqualTo(2));
                Assert.That(tx2.DangerousInstance, Is.Not.Null);
                await using (var tx3 = await cn.BeginStackedTransactionAsync())
                {
                    Assert.That(tx.RefCount, Is.EqualTo(3));
                    Assert.That(tx.DangerousInstance, Is.Not.Null);
                    Assert.That(tx3.RefCount, Is.EqualTo(3));
                    Assert.That(tx3.DangerousInstance, Is.Not.Null);
                }
                Assert.That(tx.RefCount, Is.EqualTo(2));
                Assert.That(tx.DangerousInstance, Is.Not.Null);
                Assert.That(tx2.RefCount, Is.EqualTo(2));
                Assert.That(tx2.DangerousInstance, Is.Not.Null);
            }
            Assert.That(tx.RefCount, Is.EqualTo(1));
            Assert.That(tx.DangerousInstance, Is.Not.Null);
        }
        Assert.That(tx.RefCount, Is.EqualTo(0));
        Assert.That(tx.DangerousInstance, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldQueryDatabase(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
        await cmd.ExecuteNonQueryAsync();
            
        cmd.CommandText = "SELECT COUNT(*) FROM test;";
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo(1));
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndCommitTransaction(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(1));
        }
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndImplicitlyRollbackTransaction(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(0));
        }
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndExplicitlyRollbackTransaction(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(0));
        }
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndCommitStackedTransactions(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx1 = await cn.BeginStackedTransactionAsync())
        {
            await using (await cn.BeginStackedTransactionAsync())
            {
                await using var cmd2 = cn.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
            }
            await using var cmd1 = cn.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
            await tx1.CommitAsync();
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(2));
        }
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndImplicitlyRollbackStackedTransactions(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (await cn.BeginStackedTransactionAsync())
        {
            await using (await cn.BeginStackedTransactionAsync())
            {
                await using var cmd2 = cn.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
            }
            await using var cmd1 = cn.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(0));
        }
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndExplicitlyRollbackStackedTransactions(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = _services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx1 = await cn.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                await using var cmd2 = cn.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
                await tx2.RollbackAsync();
            }
            await using var cmd1 = cn.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
            await tx1.RollbackAsync();
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(0));
        }
    }
   

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateOnIsolatedScopes(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();
        
        using var outerScope = _services.CreateScope();
        var outerScopedConnectionFactory = outerScope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();
        await using var outerCn = await outerScopedConnectionFactory.CreateScopedConnectionAsync();

        async Task Test(bool commit)
        {
            await Task.Delay(10);
            
            // ReSharper disable once AccessToDisposedClosure
            using var scope = outerScope.ServiceProvider.CreateScope();
            var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedSystemConnectionFactory>();

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();
            
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(tx.RefCount, Is.EqualTo(1));
                
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();

            if (commit)
            {
                await tx.CommitAsync();    
            }
        }

        var tasks = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            var i1 = i;
            tasks.Add(Task.Run(() => Test((i1 & 1) == 0)));
        }

        await Task.WhenAll(tasks);
        
        await using (var cmd = outerCn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(5));
        }
    }
}

