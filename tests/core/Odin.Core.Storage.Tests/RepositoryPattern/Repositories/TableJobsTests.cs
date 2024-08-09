using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Storage.RepositoryPattern.Connection.System;
using Odin.Core.Storage.RepositoryPattern.Repositories.System;

namespace Odin.Core.Storage.Tests.RepositoryPattern.Repositories;

public class TableJobsTests
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    [SetUp]
    public void Setup()
    {
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_testDir, true);
    }

    [Test]
    public async Task ItShouldDoTheThingOnSqlite()
    {
        var dbPath = Path.Combine(_testDir, "test.db");
        var connectionFactory = new SqliteSystemDbConnectionFactory($"Data Source={dbPath}");
        var tableJobs = new TableJobs(connectionFactory);
        await ItShouldDoTheThing(tableJobs);
    }

    [Test]
    public async Task ItShouldDoTheThingOnNpgsql()
    {
        var connectionFactory = new NpgsqlSystemDbConnectionFactory("Host=localhost;Port=5432;Username=test;Password=test;Database=systemdb");
        var tableJobs = new TableJobs(connectionFactory);
        await ItShouldDoTheThing(tableJobs);
    }

    [Test]
    public async Task ItShouldDoTheThingUsingSqliteIoc()
    {
        var serviceCollection = new ServiceCollection();

        var systemDbPath = Path.Combine(_testDir, "system.db");
        var connectionFactory = new SqliteSystemDbConnectionFactory($"Data Source={systemDbPath}");

        serviceCollection.AddSingleton<ISystemDbConnectionFactory>(connectionFactory);
        serviceCollection.AddTransient<TableJobs>();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var tableJobs = serviceProvider.GetRequiredService<TableJobs>();

        await ItShouldDoTheThing(tableJobs);
    }

    [Test]
    public async Task ItShouldDoTheThingUsingNpgsqlIoc()
    {
        var serviceCollection = new ServiceCollection();

        var connectionFactory = new NpgsqlSystemDbConnectionFactory("Host=localhost;Port=5432;Username=test;Password=test;Database=systemdb");

        serviceCollection.AddSingleton<ISystemDbConnectionFactory>(connectionFactory);
        serviceCollection.AddTransient<TableJobs>();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var tableJobs = serviceProvider.GetRequiredService<TableJobs>();

        await ItShouldDoTheThing(tableJobs);
    }

    private async Task ItShouldDoTheThing(TableJobs tableJobs)
    {
        await tableJobs.EnsureTableExists(true);

        var job = NewJobsRecord();
        var insertResult = await tableJobs.Insert(job);
        Assert.AreEqual(1, insertResult);

        var count = await tableJobs.GetCountAsync();
        Assert.AreEqual(1, count);

        var jobClone = await tableJobs.Get(job.id);
        Assert.AreEqual(job.id, jobClone.id);

        await tableJobs.DeleteExpiredJobsUsingDapper();
        count = await tableJobs.GetCountAsync();
        Assert.AreEqual(0, count);

        await tableJobs.InsertMany([
                NewJobsRecord(),
                NewJobsRecord(),
                NewJobsRecord()
            ],
            true);

        count = await tableJobs.GetCountAsync();
        Assert.AreEqual(3, count);

        await tableJobs.InsertMany([
                NewJobsRecord(),
                NewJobsRecord(),
                NewJobsRecord()
            ],
            false);

        count = await tableJobs.GetCountAsync();
        Assert.AreEqual(3, count);
    }

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
            expiresAt = DateTimeOffset.Now.AddDays(-1).ToUnixTimeMilliseconds(),
            correlationId = "correlationContext.Id",
            jobType = "job.GetType().AssemblyQualifiedName",
            jobData = "job.SerializeJobData()",
            jobHash = null,
            lastError = null,
        };
    }


}