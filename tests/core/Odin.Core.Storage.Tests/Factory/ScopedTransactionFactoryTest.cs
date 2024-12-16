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

public class ScopedTransactionFactoryTest : IocTestBase
{
    [TearDown]
    public override void TearDown()
    {
        DropTestDatabaseAsync().Wait();
        base.TearDown();
    }
    
    //

    private async Task CreateTestDatabaseAsync()
    {
        var scopedTransactionFactory = Services.Resolve<ScopedSystemTransactionFactory>();
        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using var cmd = tx.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (name TEXT);";
        await cmd.ExecuteNonQueryAsync();

        tx.Commit();
    }
    
    private async Task DropTestDatabaseAsync()
    {
        var scopedTransactionFactory = Services.Resolve<ScopedSystemTransactionFactory>();
        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using var cmd = tx.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test;";
        await cmd.ExecuteNonQueryAsync();

        tx.Commit();
    }
  
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCreateScopedTransactions(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndCommitTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();

            await using var cmd2 = tx.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd2.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(1));
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndImplicitlyRollbackTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using var cmd = tx.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();

            await using var cmd2 = tx.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd2.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(1));
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndCommitStackedTransactions(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
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
            tx.Commit();
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldUpdateAndImplicitlyRollbackStackedTransactions(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
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
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCreateCmdWithParams(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using var cmd1 = tx.CreateCommand();
        var nameParam = cmd1.CreateParameter();
        nameParam.ParameterName = "@name";
        nameParam.Value = "test";
        cmd1.Parameters.Add(nameParam);
        cmd1.CommandText = "INSERT INTO test (name) VALUES (@name);";

        await cmd1.ExecuteNonQueryAsync();
        tx.Commit();

        await using var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync();
        await using var cmd2 = tx2.CreateCommand();
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
        
        await using var outerScope = Services.BeginLifetimeScope();
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
        
        await using var outerTx = await outerScopedTransactionFactory.BeginStackedTransactionAsync();
        await using (var cmd = outerTx.CreateCommand())
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
    public async Task OnlyOuterMostCommitMatters(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx1 = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            await using (var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync())
            {
                await using var cmd2 = tx2.CreateCommand();
                cmd2.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd2.ExecuteNonQueryAsync();
                tx2.Commit();
            }
            await using var cmd = tx1.CreateCommand();
            cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
            await cmd.ExecuteNonQueryAsync();
        }

        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        await using (var cn = await scopedConnectionFactory.CreateScopedConnectionAsync())
        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM test;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.That(result, Is.EqualTo(0));
        }
    }
}

