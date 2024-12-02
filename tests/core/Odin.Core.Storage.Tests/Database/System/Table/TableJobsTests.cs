using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Core.Storage.Tests.Database.System.Table;

public class TableJobsTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldCountJobs(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var jobs = scope.Resolve<TableJobs>();

        var count = await jobs.GetCountAsync();
        Assert.That(count, Is.EqualTo(0));

        var record = NewJobsRecord();
        await jobs.InsertAsync(record);
        
        count = await jobs.GetCountAsync();
        Assert.That(count, Is.EqualTo(1));
    }
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldGetTheNextJob(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var jobs = scope.Resolve<TableJobs>();
        
        var nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.Null);
        
        var fiveInTheFuture = DateTimeOffset.Now.AddSeconds(5).ToUnixTimeMilliseconds();
        var zeroInThePast = DateTimeOffset.Now.AddSeconds(0).ToUnixTimeMilliseconds();
        var tenInThePast = DateTimeOffset.Now.AddSeconds(-10).ToUnixTimeMilliseconds();
        var fifteenInThePast = DateTimeOffset.Now.AddSeconds(-15).ToUnixTimeMilliseconds();
        var twentyInThePast = DateTimeOffset.Now.AddSeconds(-20).ToUnixTimeMilliseconds();
      
        var r4 = NewJobsRecord();
        r4.name = "plus 5";
        r4.nextRun = fiveInTheFuture;
        await jobs.InsertAsync(r4);
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(fiveInTheFuture));
        
        var r1 = NewJobsRecord();
        r1.name = "minus 10";
        r1.nextRun = tenInThePast;
        await jobs.InsertAsync(r1);
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(tenInThePast));
        
        var r2 = NewJobsRecord();
        r2.name = "zero";
        r2.nextRun = zeroInThePast;
        await jobs.InsertAsync(r2);
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(tenInThePast));

        var r3 = NewJobsRecord();
        r3.name = "minus 20";
        r3.nextRun = twentyInThePast;
        await jobs.InsertAsync(r3);
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(twentyInThePast));
        
        var r5 = NewJobsRecord();
        r5.name = "minus 15";
        r5.nextRun = fifteenInThePast;
        r5.priority = 0; // TOP!
        await jobs.InsertAsync(r5);

        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(twentyInThePast));

        {
            var nextJob = await jobs.GetNextScheduledJobAsync();
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("minus 15")); // because it has higher priority than minus 20
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        {
            var nextJob = await jobs.GetNextScheduledJobAsync();
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("minus 20"));
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(tenInThePast));
        
        {
            var nextJob = await jobs.GetNextScheduledJobAsync();
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("minus 10"));
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(zeroInThePast));
        
        {
            var nextJob = await jobs.GetNextScheduledJobAsync();
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("zero"));
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        nextRun = await jobs.GetNextRunTimeAsync();
        Assert.That(nextRun, Is.EqualTo(fiveInTheFuture));
        
        {
            var nextJob = await jobs.GetNextScheduledJobAsync();
            Assert.That(nextJob, Is.Null);
        }
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldGetJobByHash(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var jobs = scope.Resolve<TableJobs>();

        var record = NewJobsRecord();
        record.jobHash = "my unique hash value";
        await jobs.InsertAsync(record);

        var job = await jobs.GetJobByHashAsync(record.jobHash);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.id, Is.EqualTo(record.id));

        job = await jobs.GetJobByHashAsync("non-existing-hash");
        Assert.That(job, Is.Null);
    }

    //

    private JobsRecord NewJobsRecord()
    {
        return new JobsRecord
        {
            id = Guid.NewGuid(),
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
            jobHash = null,
            lastError = null,
        };
    }
}