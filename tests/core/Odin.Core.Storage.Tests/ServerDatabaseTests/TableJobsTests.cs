using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.ServerDatabase;

namespace Odin.Core.Storage.Tests.ServerDatabaseTests;

public class TableJobsTests
{
    [Test]
    public async Task ItShouldCountJobs()
    {
        using var db = new ServerDatabase("");
        using var cn = db.CreateDisposableConnection();
        db.CreateDatabase();
        
        var count = await db.tblJobs.GetCountAsync(cn);
        Assert.That(count, Is.EqualTo(0));

        var record = NewJobsRecord();
        db.tblJobs.Insert(cn, record);
        
        count = await db.tblJobs.GetCountAsync(cn);
        Assert.That(count, Is.EqualTo(1));
    }
    
    //
    
    [Test]
    public async Task ItShouldGetTheNextJob()
    {
        using var db = new ServerDatabase("");
        using var cn = db.CreateDisposableConnection();
        db.CreateDatabase();
        
        var nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.Null);
        
        var fiveInTheFuture = DateTimeOffset.Now.AddSeconds(5).ToUnixTimeMilliseconds();
        var zeroInThePast = DateTimeOffset.Now.AddSeconds(0).ToUnixTimeMilliseconds();
        var tenInThePast = DateTimeOffset.Now.AddSeconds(-10).ToUnixTimeMilliseconds();
        var fifteenInThePast = DateTimeOffset.Now.AddSeconds(-15).ToUnixTimeMilliseconds();
        var twentyInThePast = DateTimeOffset.Now.AddSeconds(-20).ToUnixTimeMilliseconds();
      
        var r4 = NewJobsRecord();
        r4.name = "plus 5";
        r4.nextRun = fiveInTheFuture;
        db.tblJobs.Insert(cn, r4);
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(fiveInTheFuture));
        
        var r1 = NewJobsRecord();
        r1.name = "minus 10";
        r1.nextRun = tenInThePast;
        db.tblJobs.Insert(cn, r1);
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(tenInThePast));
        
        var r2 = NewJobsRecord();
        r2.name = "zero";
        r2.nextRun = zeroInThePast;
        db.tblJobs.Insert(cn, r2);
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(tenInThePast));

        var r3 = NewJobsRecord();
        r3.name = "minus 20";
        r3.nextRun = twentyInThePast;
        db.tblJobs.Insert(cn, r3);
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(twentyInThePast));
        
        var r5 = NewJobsRecord();
        r5.name = "minus 15";
        r5.nextRun = fifteenInThePast;
        r5.priority = 0; // TOP!
        db.tblJobs.Insert(cn, r5);

        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(twentyInThePast));

        {
            var nextJob = await db.tblJobs.GetNextScheduledJob(cn);
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("minus 15")); // because it has higher priority than minus 20
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        {
            var nextJob = await db.tblJobs.GetNextScheduledJob(cn);
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("minus 20"));
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(tenInThePast));
        
        {
            var nextJob = await db.tblJobs.GetNextScheduledJob(cn);
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("minus 10"));
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(zeroInThePast));
        
        {
            var nextJob = await db.tblJobs.GetNextScheduledJob(cn);
            Assert.That(nextJob, Is.Not.Null);
            Assert.That(nextJob!.name, Is.EqualTo("zero"));
            Assert.That(nextJob!.state, Is.EqualTo((int)JobState.Preflight));
        }
        
        nextRun = await db.tblJobs.GetNextRunTime(cn);
        Assert.That(nextRun, Is.EqualTo(fiveInTheFuture));
        
        {
            var nextJob = await db.tblJobs.GetNextScheduledJob(cn);
            Assert.That(nextJob, Is.Null);
        }
        
    }
    
    
    //

    [Test]
    public async Task ItShouldGetJobByHash()
    {
        using var db = new ServerDatabase("");
        using var cn = db.CreateDisposableConnection();
        db.CreateDatabase();

        var record = NewJobsRecord();
        record.jobHash = "my unique hash value";
        db.tblJobs.Insert(cn, record);

        var job = await db.tblJobs.GetJobByHash(cn, record.jobHash);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.id, Is.EqualTo(record.id));

        job = await db.tblJobs.GetJobByHash(cn, "non-existing-hash");
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