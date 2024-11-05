using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Connection;
using Odin.Core.Storage.Database.Connection.System;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.Database.Connection;

//SEB: Ach...  make an effort to abstract away connection strings and (more) factory etc. Do it!

public class ScopedConnectionFactoryTest
{
    private string _tempFolder;
    private ILifetimeScope _container = null!;
    private LogEventMemoryStore _logEventMemoryStore = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = TempDirectory.Create();
    }

    [TearDown]
    public void TearDown()
    {
        if (_container != null)
        {
            DropTestDatabaseAsync().Wait();
            _container.Dispose();
        }
        Directory.Delete(_tempFolder, true);
        LogEvents.AssertEvents(_logEventMemoryStore.GetLogEvents());
    }

    private void RegisterServices(DatabaseType databaseType)
    {
        _logEventMemoryStore = new LogEventMemoryStore();

        var builder = new ContainerBuilder();

        if (databaseType == DatabaseType.Sqlite)
        {
            var connectionString = $"Data Source={Path.Combine(_tempFolder, "system-test.db")};Pooling=True;Cache=Shared;";
            builder
                .RegisterInstance(new SqliteSystemDbConnectionFactory(connectionString))
                .As<ISystemDbConnectionFactory>();
        }
        else if (databaseType == DatabaseType.PostgreSql)
        {
            var connectionString = "Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin";            
            builder
                .RegisterInstance(new PgsqlSystemDbConnectionFactory(connectionString))
                .As<ISystemDbConnectionFactory>();
        }

        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<ScopedSystemConnectionFactory>(_logEventMemoryStore))
            .As<ILogger<ScopedSystemConnectionFactory>>();
        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(_logEventMemoryStore))
            .As<ILogger<ScopedIdentityConnectionFactory>>();

        builder.RegisterType<ScopedSystemConnectionFactory>()
            .AsSelf()
            .InstancePerLifetimeScope();

        _container = builder.Build();
    }

    private async Task CreateTestDatabaseAsync()
    {
        var scopedConnectionFactory = _container.Resolve<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd = cn.Instance.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (name TEXT);";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DropTestDatabaseAsync()
    {
        var scopedConnectionFactory = _container.Resolve<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd = cn.Instance.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test;";
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCreateScopedConnections(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        ScopedSystemConnectionFactory.ConnectionAccessor cn;
        await using (cn = await scopedConnectionFactory.CreateScopedConnectionAsync())
        {
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(cn.Instance, Is.Not.Null);
            await using (var cn1 = await scopedConnectionFactory.CreateScopedConnectionAsync())
            {
                Assert.That(cn.RefCount, Is.EqualTo(2));
                Assert.That(cn.Instance, Is.Not.Null);
                Assert.That(cn1.RefCount, Is.EqualTo(2));
                Assert.That(cn1.Instance, Is.SameAs(cn.Instance));
            }
            await using (var cn2 = await scopedConnectionFactory.CreateScopedConnectionAsync())
            {
                Assert.That(cn.RefCount, Is.EqualTo(2));
                Assert.That(cn.Instance, Is.Not.Null);
                Assert.That(cn2.RefCount, Is.EqualTo(2));
                Assert.That(cn2.Instance, Is.Not.Null);
                await using (var cn3 = await scopedConnectionFactory.CreateScopedConnectionAsync())
                {
                    Assert.That(cn.RefCount, Is.EqualTo(3));
                    Assert.That(cn.Instance, Is.Not.Null);
                    Assert.That(cn3.RefCount, Is.EqualTo(3));
                    Assert.That(cn3.Instance, Is.Not.Null);
                }
                Assert.That(cn.RefCount, Is.EqualTo(2));
                Assert.That(cn.Instance, Is.Not.Null);
                Assert.That(cn2.RefCount, Is.EqualTo(2));
                Assert.That(cn2.Instance, Is.Not.Null);
            }
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(cn.Instance, Is.Not.Null);
        }
        Assert.That(cn.RefCount, Is.EqualTo(0));
        Assert.That(cn.Instance, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCreateScopedTransactions(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        Assert.That(cn.RefCount, Is.EqualTo(1));
        Assert.That(cn.Instance, Is.Not.Null);

        ScopedSystemConnectionFactory.TransactionAccessor tx;
        await using (tx = await cn.BeginStackedTransactionAsync())
        {
            Assert.That(tx.RefCount, Is.EqualTo(1));
            Assert.That(tx.Instance, Is.Not.Null);
            await using (var tx1 = await cn.BeginStackedTransactionAsync())
            {
                Assert.That(tx.RefCount, Is.EqualTo(2));
                Assert.That(tx.Instance, Is.Not.Null);
                Assert.That(tx1.RefCount, Is.EqualTo(2));
                Assert.That(tx1.Instance, Is.SameAs(tx.Instance));
            }
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                Assert.That(tx.RefCount, Is.EqualTo(2));
                Assert.That(tx.Instance, Is.Not.Null);
                Assert.That(tx2.RefCount, Is.EqualTo(2));
                Assert.That(tx2.Instance, Is.Not.Null);
                await using (var tx3 = await cn.BeginStackedTransactionAsync())
                {
                    Assert.That(tx.RefCount, Is.EqualTo(3));
                    Assert.That(tx.Instance, Is.Not.Null);
                    Assert.That(tx3.RefCount, Is.EqualTo(3));
                    Assert.That(tx3.Instance, Is.Not.Null);
                }
                Assert.That(tx.RefCount, Is.EqualTo(2));
                Assert.That(tx.Instance, Is.Not.Null);
                Assert.That(tx2.RefCount, Is.EqualTo(2));
                Assert.That(tx2.Instance, Is.Not.Null);
            }
            Assert.That(tx.RefCount, Is.EqualTo(1));
            Assert.That(tx.Instance, Is.Not.Null);
        }
        Assert.That(tx.RefCount, Is.EqualTo(0));
        Assert.That(tx.Instance, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldQueryDatabase(DatabaseType databaseType)
    {
        RegisterServices(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.Instance.CreateCommand();

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

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.Instance.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using (var cmd = cn.Instance.CreateCommand())
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

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.Instance.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = cn.Instance.CreateCommand())
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

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.Instance.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            await tx.RollbackAsync();
        }

        await using (var cmd = cn.Instance.CreateCommand())
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

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx1 = await cn.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                await using var cmd2 = cn.Instance.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
            }
            await using var cmd1 = cn.Instance.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
            await tx1.CommitAsync();
        }

        await using (var cmd = cn.Instance.CreateCommand())
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

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx1 = await cn.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                await using var cmd2 = cn.Instance.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
            }
            await using var cmd1 = cn.Instance.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
        }

        await using (var cmd = cn.Instance.CreateCommand())
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

        await using var scope = _container.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx1 = await cn.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                await using var cmd2 = cn.Instance.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
                await tx2.RollbackAsync();
            }
            await using var cmd1 = cn.Instance.CreateCommand();
            cmd1.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd1.ExecuteNonQueryAsync();
            await tx1.RollbackAsync();
        }

        await using (var cmd = cn.Instance.CreateCommand())
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
        
        await using var outerScope = _container.BeginLifetimeScope();
        var outerScopedConnectionFactory = outerScope.Resolve<ScopedSystemConnectionFactory>();
        await using var outerCn = await outerScopedConnectionFactory.CreateScopedConnectionAsync();

        async Task Test(bool  commit)
        {
            await Task.Delay(10);
            
            await using var scope = _container.BeginLifetimeScope();
            var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();
            
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(tx.RefCount, Is.EqualTo(1));
                
            await using var cmd = cn.Instance.CreateCommand();
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
        
        await using (var cmd = outerCn.Instance.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(5));
        }
    }
    
    
    
    // [Test]
    // public async Task TestMethod1()
    // {
    //     // IMPORTANT: "scope" below means a "scope" in the context of a DI container.
    //
    //     // Included for completeness:
    //     var serviceProvider = new ServiceContainer();
    //
    //     // Included for completeness. Normally handled by the DI container.
    //     using var scope = serviceProvider.CreateScope();
    //
    //     // Included for completeness. Will normally be injected where ever a connection is needed.
    //     var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedConnectionFactory>();
    //
    //     // Connections are "shared" in the same scope. This is required for transactions.
    //     await using var connection = await scopedConnectionFactory.CreateScopedConnectionAsync();
    //
    //     // Transactions are "shared" in the same scope. This is required for nested transactions.
    //     await using var transaction = await connection.BeginStackedTransactionAsync();
    //
    //     //
    //     // At this point, anything ON THE SAME SCOPE that calls CreateScopedConnectionAsync will get the same connection
    //     // (and transaction) as the one created above. This includes any class that is injected directly or indirectly
    //     // with ScopedConnectionFactory.
    //     //
    //
    //     await using var cmd = connection.Instance.CreateCommand();
    //     cmd.CommandText = "SELECT 1";
    //     await cmd.ExecuteNonQueryAsync();
    //
    //     // Outermost explicit commit is required transactions, otherwise the transaction will be rolled back.
    //     await transaction.CommitAsync();
    //
    //
    //
    //     Assert.Pass();
    //
    //
    //     // Act
    //
    //     // Assert
    // }

}

