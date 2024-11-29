using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.Factory;

public class ScopedTransactionFactoryTest
{
    private string _tempFolder;
    private ILifetimeScope _services = null!;
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
    }
    
    //

    private void RegisterServices(DatabaseType databaseType)
    {
        _logEventMemoryStore = new LogEventMemoryStore();
        
        var services = new ServiceCollection();
        services.AddSingleton(TestLogFactory.CreateConsoleLogger<ScopedSystemConnectionFactory>(_logEventMemoryStore));
        services.AddSingleton(TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(_logEventMemoryStore));

        var builder = new ContainerBuilder();
        builder.Populate(services);

        builder.AddDatabaseCacheServices();
        switch (databaseType)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(_tempFolder, "system-test.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices("Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin");
                break;
            default:
                throw new Exception("Unsupported database type");
        }
       
        _services = builder.Build();
    }

    private async Task CreateTestDatabaseAsync()
    {
        var scopedTransactionFactory = _services.Resolve<ScopedSystemTransactionFactory>();
        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using var cmd = tx.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (name TEXT);";
        await cmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }
    
    private async Task DropTestDatabaseAsync()
    {
        var scopedTransactionFactory = _services.Resolve<ScopedSystemTransactionFactory>();
        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using var cmd = tx.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test;";
        await cmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }
  
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCreateScopedTransactions(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();
        Assert.That(tx.Connection.RefCount, Is.EqualTo(1));
        Assert.That(tx.Transaction.RefCount, Is.EqualTo(1));
        Assert.That(tx.Connection.DangerousInstance, Is.Not.Null);
        Assert.That(tx.Transaction.DangerousInstance, Is.Not.Null);

        await using (var tx1 = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            Assert.That(tx.Connection.RefCount, Is.EqualTo(2));
            Assert.That(tx.Transaction.RefCount, Is.EqualTo(2));
            Assert.That(tx.Connection.DangerousInstance, Is.Not.Null);
            Assert.That(tx.Transaction.DangerousInstance, Is.Not.Null);
            Assert.That(tx1.Connection.RefCount, Is.EqualTo(2));
            Assert.That(tx1.Transaction.RefCount, Is.EqualTo(2));
            Assert.That(tx1.Connection.DangerousInstance, Is.SameAs(tx.Connection.DangerousInstance));
            Assert.That(tx1.Transaction.DangerousInstance, Is.SameAs(tx.Transaction.DangerousInstance));
        }
        await using (var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            Assert.That(tx.Connection.RefCount, Is.EqualTo(2));
            Assert.That(tx.Transaction.RefCount, Is.EqualTo(2));
            Assert.That(tx.Connection.DangerousInstance, Is.Not.Null);
            Assert.That(tx.Transaction.DangerousInstance, Is.Not.Null);
            Assert.That(tx2.Connection.RefCount, Is.EqualTo(2));
            Assert.That(tx2.Transaction.RefCount, Is.EqualTo(2));
            Assert.That(tx2.Connection.DangerousInstance, Is.SameAs(tx.Connection.DangerousInstance));
            Assert.That(tx2.Transaction.DangerousInstance, Is.SameAs(tx.Transaction.DangerousInstance));
            await using (var tx3 = await scopedTransactionFactory.BeginStackedTransactionAsync())
            {
                Assert.That(tx.Connection.RefCount, Is.EqualTo(3));
                Assert.That(tx.Transaction.RefCount, Is.EqualTo(3));
                Assert.That(tx.Connection.DangerousInstance, Is.Not.Null);
                Assert.That(tx.Transaction.DangerousInstance, Is.Not.Null);
                Assert.That(tx3.Connection.RefCount, Is.EqualTo(3));
                Assert.That(tx3.Transaction.RefCount, Is.EqualTo(3));
                Assert.That(tx3.Connection.DangerousInstance, Is.SameAs(tx.Connection.DangerousInstance));
                Assert.That(tx3.Transaction.DangerousInstance, Is.SameAs(tx.Transaction.DangerousInstance));
            }
            Assert.That(tx.Connection.RefCount, Is.EqualTo(2));
            Assert.That(tx.Transaction.RefCount, Is.EqualTo(2));
            Assert.That(tx.Connection.DangerousInstance, Is.Not.Null);
            Assert.That(tx.Transaction.DangerousInstance, Is.Not.Null);
            Assert.That(tx2.Connection.RefCount, Is.EqualTo(2));
            Assert.That(tx2.Transaction.RefCount, Is.EqualTo(2));
        }
        Assert.That(tx.Connection.RefCount, Is.EqualTo(1));
        Assert.That(tx.Transaction.RefCount, Is.EqualTo(1));
        Assert.That(tx.Connection.DangerousInstance, Is.Not.Null);
        Assert.That(tx.Transaction.DangerousInstance, Is.Not.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateAndCommitTransaction(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
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

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
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

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
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

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using (var tx1 = await scopedTransactionFactory.BeginStackedTransactionAsync())
            {
                await using var cmd2 = tx1.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
            }
            await using var cmd1 = tx.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
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

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync())
            {
                await using var cmd2 = tx2.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
            }
            await using var cmd1 = tx.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
        }

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
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

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync())
            {
                await using var cmd2 = tx2.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
                await tx2.RollbackAsync();
            }
            await using var cmd1 = tx.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(0));
        }
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCreateCmdWithParams(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = _services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using var cmd1 = tx.CreateCommand();
        var nameParam = cmd1.CreateParameter();
        nameParam.ParameterName = "@name";
        nameParam.Value = "test";
        cmd1.Parameters.Add(nameParam);
        cmd1.CommandText = "INSERT INTO test (name) VALUES (@name);";

        await cmd1.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        await using var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync();
        await using var cmd2 = tx2.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM test;";
        var result2 = await cmd2.ExecuteScalarAsync();
        Assert.That(result2, Is.EqualTo(1));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldUpdateOnIsolatedScopes(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();
        
        await using var outerScope = _services.BeginLifetimeScope();
        var outerScopedTransactionFactory = outerScope.Resolve<ScopedSystemTransactionFactory>();

        async Task Test(bool commit)
        {
            await Task.Delay(10);
            
            // ReSharper disable once AccessToDisposedClosure
            await using var scope = outerScope.BeginLifetimeScope();
            var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();
            await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();
            
            Assert.That(tx.Connection.RefCount, Is.EqualTo(1));
            Assert.That(tx.Transaction.RefCount, Is.EqualTo(1));
                
            await using var cmd = tx.CreateCommand();
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
        
        await using var outerTx = await outerScopedTransactionFactory.BeginStackedTransactionAsync();
        await using (var cmd = outerTx.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(5));
        }
    }
    
    //

}

