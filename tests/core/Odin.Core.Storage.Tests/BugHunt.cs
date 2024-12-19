using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Storage.Tests;

#nullable enable

public class DemoTests : IocTestBase
{
    [Test, Explicit]
    public async Task D01_connect_sqlite_double_transaction_exception()
    {
        // Demo only, don't do this

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(TempFolder, "identity-test.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true
        }.ToString();

        DbConnection? cn = null;
        try
        {
            cn = await SqliteConcreteConnectionFactory.Create(connectionString);
            var tx1 = await cn.BeginTransactionAsync();

            // tx1.Dispose();
            var tx2 = await cn.BeginTransactionAsync(); // => "System.InvalidOperationException : SqliteConnection does not support nested transactions."
        }
        finally
        {
            cn?.Dispose();
        }

        Assert.Pass();
    }

    //

    [Test, Explicit]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task D02_Connect_Sqlite_Without_Di(DatabaseType databaseType)
    {
        // Demo only, don't do this

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(TempFolder, "identity-test.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true
        }.ToString();

        var lifetimeScopeMock = new Mock<ILifetimeScope>();
        lifetimeScopeMock.Setup(x => x.Tag).Returns("some-tag");

        var logger = TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(LogEventMemoryStore, LogEventLevel.Verbose);
        var cacheHelper = new CacheHelper("whatever");
        var counters = new DatabaseCounters();
        var sqliteIdentityDbConnectionFactory = new SqliteIdentityDbConnectionFactory(connectionString);

        var factory = new ScopedIdentityConnectionFactory(
            lifetimeScopeMock.Object,
            logger,
            sqliteIdentityDbConnectionFactory,
            cacheHelper,
            counters
        );

        IConnectionWrapper? cn = null;
        ITransactionWrapper? tx1 = null;
        ITransactionWrapper? tx2 = null;
        try
        {
            cn = await factory.CreateScopedConnectionAsync();
            tx1 = await cn.BeginStackedTransactionAsync();
            tx2 = await cn.BeginStackedTransactionAsync();
        }
        finally
        {
            if (tx2 != null)
            {
                await tx2.DisposeAsync();
            }
            if (tx1 != null)
            {
                await tx1.DisposeAsync();
            }
            if (cn != null)
            {
                await cn.DisposeAsync();
            }
        }

        Assert.Pass();
    }

    //

    [Test, Explicit]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task D03_Connect_Sqlite_With_Di(DatabaseType databaseType)
    {
        var factory = Services.Resolve<ScopedIdentityConnectionFactory>();

        await using var cn = await factory.CreateScopedConnectionAsync();
        await using var tx1 = await cn.BeginStackedTransactionAsync();
        await using var tx2 = await cn.BeginStackedTransactionAsync();

        Assert.Pass();
    }

    //

    [Test, Explicit]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task D04_Connect_Sqlite_Parallel(DatabaseType databaseType)
    {
        var logger = TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(LogEventMemoryStore, LogEventLevel.Verbose);

        const int taskCount = 100;

        await RegisterServicesAsync(databaseType);

        logger.LogInformation("Starting D04_Connect_Sqlite_Parallel");

        var tasks = new List<Task>();

        for (var idx = 0; idx < taskCount; idx++)
        {
            var i = idx;
            tasks.Add(Task.Run(async () =>
            {
                await using var scope = Services.BeginLifetimeScope();
                var factory = scope.Resolve<ScopedIdentityConnectionFactory>();

                await Task.Delay(1000);

                logger.LogInformation("Task {i} starting", i);

                await using var cn = await factory.CreateScopedConnectionAsync();
                await using var tx1 = await cn.BeginStackedTransactionAsync();
                await using var tx2 = await cn.BeginStackedTransactionAsync();
                await using var cmd = cn.CreateCommand();

                cmd.CommandText = "INSERT INTO keyValue (identityId,key,data) VALUES (@identityId,@key,@data)";
                var insertParam1 = cmd.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                cmd.Parameters.Add(insertParam1);
                var insertParam2 = cmd.CreateParameter();
                insertParam2.ParameterName = "@key";
                cmd.Parameters.Add(insertParam2);
                var insertParam3 = cmd.CreateParameter();
                insertParam3.ParameterName = "@data";
                cmd.Parameters.Add(insertParam3);
                insertParam1.Value = Guid.NewGuid().ToByteArray();
                insertParam2.Value = Guid.NewGuid().ToByteArray();
                insertParam3.Value = Guid.NewGuid().ToByteArray();

                await cmd.ExecuteNonQueryAsync();

                tx1.Commit();

                logger.LogInformation("Task {i} completed", i);
            }));
        }



        logger.LogInformation(">>>>>>>>>>>>>>>>>");
        await Task.WhenAll(tasks);
        logger.LogInformation("<<<<<<<<<<<<<<<<<");


        Assert.Pass();
    }



}


/*

"cannot start a transaction within a transaction"

Notice that this error is coming from raw sqlite itself (sqlite3.c), not from the Microsoft.Data.Sqlite library,
which would normally be the case.

2024-12-05T13:28:14.2802103Z   Failed PerformanceTest03B(Sqlite) [3 s]
2024-12-05T13:28:14.2802897Z   Error Message:
2024-12-05T13:28:14.2804410Z    System.AggregateException : One or more errors occurred. (SQLite Error 1: 'cannot start a transaction within a transaction'.)
2024-12-05T13:28:14.2805982Z   ----> Microsoft.Data.Sqlite.SqliteException : SQLite Error 1: 'cannot start a transaction within a transaction'.
2024-12-05T13:28:14.2807039Z   Stack Trace:
2024-12-05T13:28:14.2807956Z      at System.Threading.Tasks.Task.WaitAllCore(Task[] tasks, Int32 millisecondsTimeout, CancellationToken cancellationToken)
2024-12-05T13:28:14.2810771Z    at Odin.Core.Storage.Tests.Database.Identity.Abstractions.DriveMainIndexPerformanceTests.PerformanceTest03B(DatabaseType databaseType) in /home/runner/work/odin-core/odin-core/tests/core/Odin.Core.Storage.Tests/Database/Identity/Abstractions/DriveMainIndexPerformanceTests.cs:line 356
2024-12-05T13:28:14.2813366Z    at NUnit.Framework.Internal.TaskAwaitAdapter.GenericAdapter`1.GetResult()
2024-12-05T13:28:14.2814361Z    at NUnit.Framework.Internal.AsyncToSyncAdapter.Await(Func`1 invoke)
2024-12-05T13:28:14.2815693Z    at NUnit.Framework.Internal.Commands.TestMethodCommand.RunTestMethod(TestExecutionContext context)
2024-12-05T13:28:14.2816957Z    at NUnit.Framework.Internal.Commands.TestMethodCommand.Execute(TestExecutionContext context)
2024-12-05T13:28:14.2818222Z    at NUnit.Framework.Internal.Commands.BeforeAndAfterTestCommand.<>c__DisplayClass1_0.<Execute>b__0()
2024-12-05T13:28:14.2819856Z    at NUnit.Framework.Internal.Commands.DelegatingTestCommand.RunTestMethodInThreadAbortSafeZone(TestExecutionContext context, Action action)
2024-12-05T13:28:14.2821008Z --SqliteException
2024-12-05T13:28:14.2821642Z    at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(Int32 rc, sqlite3 db)
2024-12-05T13:28:14.2822616Z    at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
2024-12-05T13:28:14.2823438Z    at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader(CommandBehavior behavior)
2024-12-05T13:28:14.2824717Z    at Microsoft.Data.Sqlite.SqliteConnectionExtensions.ExecuteNonQuery(SqliteConnection connection, String commandText, SqliteParameter[] parameters)
2024-12-05T13:28:14.2826392Z    at Microsoft.Data.Sqlite.SqliteTransaction..ctor(SqliteConnection connection, IsolationLevel isolationLevel, Boolean deferred)
2024-12-05T13:28:14.2827831Z    at System.Data.Common.DbConnection.BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
2024-12-05T13:28:14.2828896Z --- End of stack trace from previous location ---
2024-12-05T13:28:14.2830754Z    at Odin.Core.Storage.Factory.ScopedConnectionFactory`1.BeginStackedTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) in /home/runner/work/odin-core/odin-core/src/core/Odin.Core.Storage/Factory/ScopedConnectionFactory.cs:line 180
2024-12-05T13:28:14.2833267Z    at Odin.Core.Storage.Factory.ScopedConnectionFactory`1.ConnectionWrapper.BeginStackedTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
2024-12-05T13:28:14.2836527Z    at Odin.Core.Storage.Database.Identity.Abstractions.MainIndexMeta.BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord, List`1 accessControlList, List`1 tagIdList) in /home/runner/work/odin-core/odin-core/src/core/Odin.Core.Storage/Database/Identity/Abstractions/MainIndexMeta.cs:line 49
2024-12-05T13:28:14.2840070Z    at Odin.Core.Storage.Database.Identity.Abstractions.MainIndexMeta.BaseUpsertEntryZapZapAsync(DriveMainIndexRecord driveMainIndexRecord, List`1 accessControlList, List`1 tagIdList) in /home/runner/work/odin-core/odin-core/src/core/Odin.Core.Storage/Database/Identity/Abstractions/MainIndexMeta.cs:line 64
2024-12-05T13:28:14.2844750Z    at Odin.Core.Storage.Database.Identity.Abstractions.MainIndexMeta.AddEntryPassalongToUpsertAsync(Guid driveId, Guid fileId, Nullable`1 globalTransitId, Int32 fileType, Int32 dataType, String senderId, Nullable`1 groupId, Nullable`1 uniqueId, Int32 archivalStatus, UnixTimeUtc userDate, Int32 requiredSecurityGroup, List`1 accessControlList, List`1 tagIdList, Int64 byteCount, Int32 fileSystemType, Int32 fileState) in /home/runner/work/odin-core/odin-core/src/core/Odin.Core.Storage/Database/Identity/Abstractions/MainIndexMeta.cs:line 766
2024-12-05T13:28:14.2849428Z    at Odin.Core.Storage.Tests.Database.Identity.Abstractions.DriveMainIndexPerformanceTests.WriteRowsAsync(Int32 threadno, Int32 iterations, MainIndexMeta metaIndex, Guid driveId) in /home/runner/work/odin-core/odin-core/tests/core/Odin.Core.Storage.Tests/Database/Identity/Abstractions/DriveMainIndexPerformanceTests.cs:line 407
2024-12-05T13:28:14.2852906Z    at Odin.Core.Storage.Tests.Database.Identity.Abstractions.DriveMainIndexPerformanceTests.<>c__DisplayClass9_0.<<PerformanceTest03B>b__0>d.MoveNext() in /home/runner/work/odin-core/odin-core/tests/core/Odin.Core.Storage.Tests/Database/Identity/Abstractions/DriveMainIndexPerformanceTests.cs:line 350
2024-12-05T13:28:14.2854910Z --- End of stack trace from previous location ---
2024-12-05T13:28:14.2856963Z    at Odin.Core.Storage.Tests.Database.Identity.Abstractions.DriveMainIndexPerformanceTests.<>c__DisplayClass9_0.<<PerformanceTest03B>b__0>d.MoveNext() in /home/runner/work/odin-core/odin-core/tests/core/Odin.Core.Storage.Tests/Database/Identity/Abstractions/DriveMainIndexPerformanceTests.cs:line 350

*/


/*

"Cannot access a disposed object"

Notice that the both command and connection are disposed before the exception is thrown. This is extremely odd.

This is coming from the JobRunnerBackgroundService

2024-12-05T01:10:52.3110231+00:00 VRB d8430620-c074-46e8-92c8-ec10c80260a9 system& Created connection ScopedConnectionFactory:"ff044baf-fae9-4307-842d-a7823d696677" on scope:JobRunnerBackgroundService:b00210c4-c9a1-48a1-af84-01a24bae33c8
2024-12-05T01:10:52.3110559+00:00 VRB d8430620-c074-46e8-92c8-ec10c80260a9 system&  Creating command ScopedConnectionFactory:"ff044baf-fae9-4307-842d-a7823d696677"
2024-12-05T01:10:52.3111198+00:00 VRB d8430620-c074-46e8-92c8-ec10c80260a9 system&   ExecuteReaderAsync ScopedDbConnection:"ff044baf-fae9-4307-842d-a7823d696677"
2024-12-05T01:10:52.3121123+00:00 INF d8430620-c074-46e8-92c8-ec10c80260a9 system& Connection "ff044baf-fae9-4307-842d-a7823d696677" was created at scope:JobRunnerBackgroundService:b00210c4-c9a1-48a1-af84-01a24bae33c8 /build/src/core/Odin.Core.Storage/Database/System/Table/TableJobs.cs:81
2024-12-05T01:10:52.3123573+00:00 VRB d8430620-c074-46e8-92c8-ec10c80260a9 system&  Disposing command ScopedDbConnection:"ff044baf-fae9-4307-842d-a7823d696677"
2024-12-05T01:10:52.3124178+00:00 VRB d8430620-c074-46e8-92c8-ec10c80260a9 system& Disposed connection ScopedConnectionFactory:"ff044baf-fae9-4307-842d-a7823d696677" on scope:JobRunnerBackgroundService:b00210c4-c9a1-48a1-af84-01a24bae33c8
2024-12-05T01:10:52.3151592+00:00 ERR d8430620-c074-46e8-92c8-ec10c80260a9 system& BackgroundService JobRunnerBackgroundService is exiting because of an unhandled exception: Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.
  at System.StubHelpers.StubHelpers.SafeHandleAddRef(SafeHandle pHandle, Boolean& success)
  at SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_prepare_v2(sqlite3 db, Byte* pSql, Int32 nBytes, IntPtr& stmt, Byte*& ptrRemain)
  at Microsoft.Data.Sqlite.SqliteCommand.PrepareAndEnumerateStatements()+MoveNext()
  at Microsoft.Data.Sqlite.SqliteCommand.GetStatements()+MoveNext()
  at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
  at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader(CommandBehavior behavior)
  at Microsoft.Data.Sqlite.SqliteCommand.ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
  at Odin.Core.Storage.Factory.ScopedConnectionFactory`1.CommandWrapper.ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) in /build/src/core/Odin.Core.Storage/Factory/ScopedConnectionFactory.cs:line 508
  at Odin.Core.Storage.Database.System.Table.TableJobs.GetNextScheduledJobAsync() in /build/src/core/Odin.Core.Storage/Database/System/Table/TableJobs.cs:line 125
  at Odin.Core.Storage.Database.System.Table.TableJobs.GetNextScheduledJobAsync()
  at Odin.Core.Storage.Database.System.Table.TableJobs.GetNextScheduledJobAsync()
  at Odin.Services.JobManagement.JobRunnerBackgroundService.ExecuteAsync(CancellationToken stoppingToken) in /build/src/services/Odin.Services/JobManagement/JobRunnerBackgroundService.cs:line 26
  at Odin.Services.Background.Services.AbstractBackgroundService.ExecuteWithCatchAllAsync(CancellationToken stoppingToken) in /build/src/services/Odin.Services/Background/Services/AbstractBackgroundService.cs:line 132

 */


/*

"Cannot access a disposed object"

This is coming from a http request.


2024-12-02T13:01:10.7629181+00:00 INF fb67dcde-77fa-4a94-ae9c-270efbe7615b queralt.dominion.id 14.192.236.121 request starting POST /api/apps/v1/notify/preauth
   2024-12-02T13:01:10.7636608+00:00 INF fb67dcde-77fa-4a94-ae9c-270efbe7615b queralt.dominion.id Scheduling ICR Key Available job
   2024-12-02T13:01:10.7637134+00:00 DBG fb67dcde-77fa-4a94-ae9c-270efbe7615b queralt.dominion.id IcrKeyUpgradeJob CreateJobHash Odin.Services.Membership.Connections.IcrKeyAvailableWorker.IcrKeyAvailableJob, Odin.Services, Version=1.0.0.0, Culture=neutral, PublicKeyToken=nullXqueralt.dominion.id
   2024-12-02T13:01:10.7637522+00:00 DBG fb67dcde-77fa-4a94-ae9c-270efbe7615b queralt.dominion.id JobManager scheduling unique job 'IcrKeyAvailableJob' id:"28051456-2487-43dc-9288-2e7bb413695f" hash:5QvXisekwH7vwEObzDQ8unCW9VSguK5Mc+DDSGp7nYU= for 2024-12-02T13:01:10.7636747+00:00
   2024-12-02T13:01:10.7664930+00:00 ERR fb67dcde-77fa-4a94-ae9c-270efbe7615b queralt.dominion.id Cannot access a disposed object.
   Object name: 'SQLitePCL.sqlite3'.
   System.ObjectDisposedException: Cannot access a disposed object.
   Object name: 'SQLitePCL.sqlite3'.
      at System.StubHelpers.StubHelpers.SafeHandleAddRef(SafeHandle pHandle, Boolean& success)
      at SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_prepare_v2(sqlite3 db, Byte* pSql, Int32 nBytes, IntPtr& stmt, Byte*& ptrRemain)
      at Microsoft.Data.Sqlite.SqliteCommand.PrepareAndEnumerateStatements()+MoveNext()
      at Microsoft.Data.Sqlite.SqliteCommand.GetStatements()+MoveNext()
      at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
      at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader(CommandBehavior behavior)
      at System.Data.Common.DbCommand.ExecuteNonQueryAsync(CancellationToken cancellationToken)
   --- End of stack trace from previous location ---
      at Odin.Core.Storage.Factory.ScopedConnectionFactory`1.CommandWrapper.ExecuteNonQueryAsync(CancellationToken cancellationToken) in /build/src/core/Odin.Core.Storage/Factory/ScopedConnectionFactory.cs:line 472
      at Odin.Core.Storage.Database.System.Table.TableJobsCRUD.TryInsertAsync(JobsRecord item) in /build/src/core/Odin.Core.Storage/Database/System/Table/TableJobsCRUD.cs:line 459
      at Odin.Core.Storage.Database.System.Table.TableJobsCRUD.TryInsertAsync(JobsRecord item) in /build/src/core/Odin.Core.Storage/Database/System/Table/TableJobsCRUD.cs:line 464
      at Odin.Core.Storage.Database.System.Table.TableJobsCRUD.TryInsertAsync(JobsRecord item) in /build/src/core/Odin.Core.Storage/Database/System/Table/TableJobsCRUD.cs:line 464
      at Odin.Services.JobManagement.JobManager.ScheduleJobAsync(AbstractJob job, JobSchedule schedule) in /build/src/services/Odin.Services/JobManagement/JobManager.cs:line 96
      at Odin.Services.Membership.Connections.IcrKeyAvailableWorker.IcrKeyAvailableScheduler.EnsureScheduledAsync(ClientAuthenticationToken token, IOdinContext odinContext, JobTokenType jobTokenType) in /build/src/services/Odin.Services/Membership/Connections/IcrKeyAvailableWorker/IcrKeyAvailableScheduler.cs:line 62
      at Odin.Hosting.Authentication.YouAuth.YouAuthAuthenticationHandler.HandleAppAuth(IOdinContext odinContext) in /build/src/apps/Odin.Hosting/Authentication/YouAuth/YouAuthAuthenticationHandler.cs:line 103
      at Odin.Hosting.Authentication.YouAuth.YouAuthAuthenticationHandler.HandleAuthenticateAsync() in /build/src/apps/Odin.Hosting/Authentication/YouAuth/YouAuthAuthenticationHandler.cs:line 51
      at Microsoft.AspNetCore.Authentication.AuthenticationHandler`1.AuthenticateAsync()
      at Microsoft.AspNetCore.Authentication.AuthenticationService.AuthenticateAsync(HttpContext context, String scheme)
      at Microsoft.AspNetCore.Authorization.Policy.PolicyEvaluator.AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
      at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.Invoke(HttpContext context)
      at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.Invoke(HttpContext context)
      at Odin.Hosting.Multitenant.MultiTenantContainerMiddleware.Invoke(HttpContext context) in /build/src/apps/Odin.Hosting/Multitenant/MultiTenantContainerMiddleware.cs:line 43
      at Microsoft.AspNetCore.ResponseCompression.ResponseCompressionMiddleware.InvokeCore(HttpContext context)
      at Odin.Hosting.Middleware.ExceptionHandlingMiddleware.Invoke(HttpContext context) in /build/src/apps/Odin.Hosting/Middleware/ExceptionHandlingMiddleware.cs:line 35

 */