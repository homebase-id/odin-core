using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Exceptions;
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldUpdateAndCommitTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            {
                await using var cmd = tx.CreateCommand();
                cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd.ExecuteNonQueryAsync();
                tx.Commit();
            }

            {
                await using var cmd2 = tx.CreateCommand();
                cmd2.CommandText = "SELECT COUNT(*) FROM test;";
                var result = await cmd2.ExecuteScalarAsync();
                Assert.That(result, Is.EqualTo(1));
            }
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldUpdateAndImplicitlyRollbackTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using (var tx = await scopedTransactionFactory.BeginStackedTransactionAsync())
        {
            {
                await using var cmd = tx.CreateCommand();
                cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
                await cmd.ExecuteNonQueryAsync();
            }

            {
                await using var cmd2 = tx.CreateCommand();
                cmd2.CommandText = "SELECT COUNT(*) FROM test;";
                var result = await cmd2.ExecuteScalarAsync();
                Assert.That(result, Is.EqualTo(1));
            }
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldCreateCmdWithParams(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateTestDatabaseAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        {
            await using var cmd1 = tx.CreateCommand();
            var nameParam = cmd1.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = "test";
            cmd1.Parameters.Add(nameParam);
            cmd1.CommandText = "INSERT INTO test (name) VALUES (@name);";
            await cmd1.ExecuteNonQueryAsync();
        }

        tx.Commit();

        {
            await using var tx2 = await scopedTransactionFactory.BeginStackedTransactionAsync();
            await using var cmd2 = tx2.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM test;";
            var result2 = await cmd2.ExecuteScalarAsync();
            Assert.That(result2, Is.EqualTo(1));
        }
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
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

    //
    // Reproduces the scenario in DriveQuery.cs:249 where a unique-constraint
    // violation is caught inside an open transaction and further SELECTs are
    // issued. On SQLite this works; on Postgres the transaction is poisoned
    // (SQLSTATE 25P02) until rolled back.
    //

    private async Task CreateUniqueTableAsync()
    {
        var scopedTransactionFactory = Services.Resolve<ScopedSystemTransactionFactory>();
        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using (var dropCmd = tx.CreateCommand())
        {
            dropCmd.CommandText = "DROP TABLE IF EXISTS test_unique;";
            await dropCmd.ExecuteNonQueryAsync();
        }

        await using (var createCmd = tx.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE test_unique (name TEXT UNIQUE);";
            await createCmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ConstraintViolationDoesNotPoisonTransaction(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await CreateUniqueTableAsync();

        await using var scope = Services.BeginLifetimeScope();
        var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();

        await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();

        await using (var insertA = tx.CreateCommand())
        {
            insertA.CommandText = "INSERT INTO test_unique (name) VALUES ('a');";
            await insertA.ExecuteNonQueryAsync();
        }

        var caught = false;
        try
        {
            await using var insertDup = tx.CreateCommand();
            insertDup.CommandText = "INSERT INTO test_unique (name) VALUES ('a');";
            await insertDup.ExecuteNonQueryAsync();
        }
        catch (OdinDatabaseException e) when (e.IsUniqueConstraintViolation)
        {
            caught = true;
        }

        Assert.That(caught, Is.True, "expected OdinDatabaseException with IsUniqueConstraintViolation");

        // On SQLite, the transaction is still usable after the violation.
        await using var selectCmd = tx.CreateCommand();
        selectCmd.CommandText = "SELECT COUNT(*) FROM test_unique;";
        var result = await selectCmd.ExecuteScalarAsync();
        Assert.That(result, Is.EqualTo(1));
    }

// #if RUN_POSTGRES_TESTS
//     [Test]
//     public async Task ConstraintViolationPoisonsTransaction_Postgres()
//     {
//         await RegisterServicesAsync(DatabaseType.Postgres);
//         await CreateUniqueTableAsync();
//
//         await using var scope = Services.BeginLifetimeScope();
//         var scopedTransactionFactory = scope.Resolve<ScopedSystemTransactionFactory>();
//
//         await using var tx = await scopedTransactionFactory.BeginStackedTransactionAsync();
//
//         await using (var insertA = tx.CreateCommand())
//         {
//             insertA.CommandText = "INSERT INTO test_unique (name) VALUES ('a');";
//             await insertA.ExecuteNonQueryAsync();
//         }
//
//         var caught = false;
//         try
//         {
//             await using var insertDup = tx.CreateCommand();
//             insertDup.CommandText = "INSERT INTO test_unique (name) VALUES ('a');";
//             await insertDup.ExecuteNonQueryAsync();
//         }
//         catch (OdinDatabaseException e) when (e.IsUniqueConstraintViolation)
//         {
//             caught = true;
//         }
//
//         Assert.That(caught, Is.True, "expected OdinDatabaseException with IsUniqueConstraintViolation");
//
//         // On Postgres, the transaction is now in an aborted state.
//         // Even a plain SELECT fails with SQLSTATE 25P02.
//         var ex = Assert.ThrowsAsync<OdinDatabaseException>(async () =>
//         {
//             await using var selectCmd = tx.CreateCommand();
//             selectCmd.CommandText = "SELECT COUNT(*) FROM test_unique;";
//             await selectCmd.ExecuteScalarAsync();
//         });
//
//         var pg = ex!.InnerException as PostgresException;
//         Assert.That(pg, Is.Not.Null, "expected inner PostgresException");
//         Assert.That(pg!.SqlState, Is.EqualTo("25P02"),
//             "expected 'current transaction is aborted' (25P02)");
//     }
// #endif
}

