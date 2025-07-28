using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests;

#nullable enable
public class OdinDatabaseExceptionTests : IocTestBase
{
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task IsUniqueConstraintViolation_PrimaryKey(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var jobs = scope.Resolve<TableJobs>();

        var jobId = Guid.NewGuid();
        var record = NewJobsRecord(jobId, null);
        await jobs.InsertAsync(record);

        var ex = Assert.ThrowsAsync<OdinDatabaseException>(async () => await jobs.InsertAsync(record));
        ClassicAssert.IsTrue(ex!.IsUniqueConstraintViolation);
    }

    //

    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task IsUniqueConstraintViolation_Index(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var jobs = scope.Resolve<TableJobs>();

        var record1 = NewJobsRecord(Guid.NewGuid(), "trigger_constraint_error");
        await jobs.InsertAsync(record1);

        var record2 = NewJobsRecord(Guid.NewGuid(), "trigger_constraint_error");

        var ex = Assert.ThrowsAsync<OdinDatabaseException>(async () => await jobs.InsertAsync(record2));
        ClassicAssert.IsTrue(ex!.IsUniqueConstraintViolation);
    }

    //

    private JobsRecord NewJobsRecord(Guid jobId, string? jobHash)
    {
        return new JobsRecord
        {
            id = jobId,
            name = "jobname",
            state = (int)JobState.Scheduled,
            priority = int.MaxValue / 2,
            nextRun = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            lastRun = null,
            runCount = 0,
            maxAttempts = 1,
            retryDelay = 100,
            onSuccessDeleteAfter = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            onFailureDeleteAfter = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            correlationId = "correlationContext.Id",
            jobType = "job.GetType().AssemblyQualifiedName",
            jobData = "job.SerializeJobData()",
            jobHash = jobHash,
            lastError = null,
        };
    }

}