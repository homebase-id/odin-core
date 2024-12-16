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
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.Factory;

public class ScopedConnectionFactoryTest : IocTestBase
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
        cmd.CommandText = "CREATE TABLE test (name TEXT);";
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCreateScopedConnections(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCreateScopedTransactions(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldQueryDatabase(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        {
            using var scope = Services.BeginLifetimeScope();
            var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(1));
        }
        
        {
            using var scope = Services.BeginLifetimeScope();
            var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(2));
        }
        
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndCommitTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
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
    [TestCase(DatabaseType.Postgres)]
    public async Task OnlyOuterMostCommitMatters(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx1 = await cn.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await cn.BeginStackedTransactionAsync())
            {
                tx2.Commit();
            }
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndImplicitlyRollbackTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndCommitStackedTransactions(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

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
            tx1.Commit();
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndImplicitlyRollbackStackedTransactions(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCreateCmdWithParamsBeforeTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await using var cmd1 = cn.CreateCommand();
        var nameParam = cmd1.CreateParameter();
        nameParam.ParameterName = "@name";
        nameParam.Value = "test";
        cmd1.Parameters.Add(nameParam);
        cmd1.CommandText = "INSERT INTO test (name) VALUES (@name);";

        await using var tx = await cn.BeginStackedTransactionAsync();
        await cmd1.ExecuteNonQueryAsync();
        tx.Commit();

        await using var cmd2 = cn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM test;";
        var result2 = await cmd2.ExecuteScalarAsync();
        Assert.That(result2, Is.EqualTo(1));
    }


    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateOnIsolatedScopes(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();
        
        using var outerScope = Services.BeginLifetimeScope();
        var outerScopedConnectionFactory = outerScope.Resolve<ScopedSystemConnectionFactory>();
        await using var outerCn = await outerScopedConnectionFactory.CreateScopedConnectionAsync();

        async Task Test(bool commit)
        {
            await Task.Delay(10);
            
            // ReSharper disable once AccessToDisposedClosure
            using var scope = outerScope.BeginLifetimeScope();
            var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var tx = await cn.BeginStackedTransactionAsync();
            
            Assert.That(cn.RefCount, Is.EqualTo(1));
            Assert.That(tx.RefCount, Is.EqualTo(1));
                
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();

            if (commit)
            {
                tx.Commit();
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
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldResolveScopeUsers(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();
        
        var scopedSystemUser = Services.Resolve<ScopedSystemUser>();
        Assert.That(await scopedSystemUser.GetCountAsync(), Is.EqualTo(0));
        
        var transientSystemUser = Services.Resolve<TransientSystemUser>();
        Assert.That(await transientSystemUser.GetCountAsync(), Is.EqualTo(0));
    }
    
    //

}

