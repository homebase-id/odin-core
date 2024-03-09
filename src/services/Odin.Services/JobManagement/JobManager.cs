using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Odin.Services.JobManagement;
#nullable enable

//

public interface IJobManager
{
    Task Initialize(Func<Task>? configureJobs = null);
    Task<JobKey> Schedule<TJob>(AbstractJobSchedule jobSchedule) where TJob : IJob;
    Task<JobResponse> GetResponse(JobKey jobKey);
    Task<(JobResponse, T?)> GetResponse<T>(JobKey jobKey) where T : class;
    Task<bool> Exists(JobKey jobKey);
    Task<bool> Delete(JobKey jobKey);
    Task<bool> Delete(AbstractJobSchedule jobSchedule);
}

//

public sealed class JobManagerConfig
{
    public string DatabaseDirectory { get; init; } = "";
    public bool ConnectionPooling { get; init; } = true;
    public int SchedulerThreadCount { get; init; }
}

//

public sealed class JobManager(
    ILogger<JobManager> logger,
    ILoggerFactory loggerFactory,
    ICorrelationContext correlationContext,
    IJobFactory jobFactory,
    IJobListener jobListener,
    JobManagerConfig config
    ) : IJobManager, IAsyncDisposable
{
    private bool _disposing;
    private readonly AsyncLock _mutex = new();
    private readonly Dictionary<string, IScheduler> _schedulers = new();

    //

    public async Task Initialize(Func<Task>? configureJobs)
    {
        Quartz.Logging.LogContext.SetCurrentLogProvider(loggerFactory);

        using (await _mutex.LockAsync())
        {
            logger.LogDebug("Creating schedulers");
            await CreateSchedulers();
        }

        if (configureJobs != null)
        {
            await configureJobs();
        }

        using (await _mutex.LockAsync())
        {
            logger.LogDebug("Starting schedulers");
            await StartSchedulers();
        }
    }

    //

    public async Task<JobKey> Schedule<TJob>(AbstractJobSchedule jobSchedule) where TJob : IJob
    {
        using (await _mutex.LockAsync())
        {
            // Sanity
            if (_disposing)
            {
                throw new JobManagerException("JobManager is shutting down");
            }

            var scheduler = GetScheduler(jobSchedule.SchedulerGroup);
            if (scheduler == null)
            {
                throw new JobManagerException($"Scheduler {jobSchedule.SchedulerGroup} does not exist");
            }

            var jobKey = await scheduler.GetScheduledJobKey(jobSchedule.SchedulingKey);
            if (jobKey != null)
            {
                logger.LogDebug("Already scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);
                return jobKey;
            }

            var (jobBuilder, triggerBuilders) = await jobSchedule.Schedule<TJob>(JobBuilder.Create<TJob>());
            if (triggerBuilders.Count == 0)
            {
                // Sanity: we don't want to schedule a job without triggers
                return new JobKey("non-scheduled-job");
            }

            jobKey = jobSchedule.CreateJobKey();
            jobBuilder.WithIdentity(jobKey);
            jobBuilder.UsingJobData(JobConstants.StatusKey, JobConstants.StatusValueAdded);
            jobBuilder.UsingJobData(JobConstants.CorrelationIdKey, correlationContext.Id);
            jobBuilder.UsingJobData(JobConstants.JobTypeName, typeof(TJob).FullName);

            var job = jobBuilder.Build();
            foreach (var triggerBuilder in triggerBuilders)
            {
                var trigger = triggerBuilder.Build();
                await scheduler.ScheduleJob(job, trigger);
            }

            logger.LogDebug("Scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);

            return jobKey;
        }
    }

    //

    public async Task<JobResponse> GetResponse(JobKey jobKey)
    {
        var response = new JobResponse
        {
            Status = JobStatus.NotFound,
            JobKey = jobKey.ToString(),
        };

        using (await _mutex.LockAsync())
        {
            var schedulerGroup = jobKey.SchedulerGroup();
            if (schedulerGroup == null)
            {
                return response;
            }

            var scheduler = GetScheduler(schedulerGroup.Value);
            if (scheduler == null)
            {
                return response;
            }

            var job = await scheduler.GetJobDetail(jobKey);
            if (job == null || !job.Key.Equals(jobKey))
            {
                return response;
            }

            var jobData = job.JobDataMap;
            jobData.TryGetString(JobConstants.StatusKey, out var status);
            jobData.TryGetString(JobConstants.JobErrorMessageKey, out var errorMessage);
            jobData.TryGetString(JobConstants.JobResponseDataKey, out var data);

            response.Status = Helpers.JobStatusFromStatusValue(status ?? "");
            response.Error = errorMessage;
            response.Data = data;

            return response;
        }
    }

    //

    public async Task<(JobResponse, T?)> GetResponse<T>(JobKey jobKey) where T : class
    {
        var response = await GetResponse(jobKey);
        if (response.Data == null)
        {
            return (response, null);
        }

        var data = OdinSystemSerializer.Deserialize<T>(response.Data);
        if (data == null)
        {
            throw new JobManagerException("Error deserializing JobResponse.Data");
        }

        return (response, data);
    }

    //

    public async Task<bool> Exists(JobKey jobKey)
    {
        using (await _mutex.LockAsync())
        {
            var schedulerGroup = jobKey.SchedulerGroup();
            if (schedulerGroup == null)
            {
                return false;
            }

            var scheduler = GetScheduler(schedulerGroup.Value);
            if (scheduler == null)
            {
                return false;
            }

            return await scheduler.CheckExists(jobKey);
        }
    }

    //

    public async Task<bool> Delete(JobKey jobKey)
    {
        //
        // Race condition in Quartz when deleting here:
        //   https://github.com/quartznet/quartznet/blob/c4d3a0a9233d48078a288691e638505116a74ca9/src/Quartz/Core/QuartzScheduler.cs#L690
        // It seems to work better if we explicitly unschedule the triggers before deleting the job.
        //
        using (await _mutex.LockAsync())
        {
            var schedulerGroup = jobKey.SchedulerGroup();
            if (schedulerGroup == null)
            {
                return false;
            }

            var scheduler = GetScheduler(schedulerGroup.Value);
            if (scheduler == null)
            {
                return false;
            }

            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            foreach (var trigger in triggers)
            {
                await scheduler.UnscheduleJob(trigger.Key);
            }

            var deleted = await scheduler.DeleteJob(jobKey);
            if (deleted)
            {
                // logger.LogDebug("Explicitly deleted {JobKey}", jobKey);
            }
            return deleted;
        }
    }

    //

    public async Task<bool> Delete(AbstractJobSchedule jobSchedule)
    {
        using (await _mutex.LockAsync())
        {
            var scheduler = GetScheduler(jobSchedule.SchedulerGroup);
            if (scheduler == null)
            {
                return false;
            }

            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(jobSchedule.SchedulingKey));
            var deleted = await scheduler.DeleteJobs(jobKeys);
            if (deleted)
            {
                // logger.LogDebug("Explicitly deleted {JobId}", jobScheduler.JobId);
            }
            return deleted;
        }
    }

    //

    public async ValueTask DisposeAsync()
    {
        using (await _mutex.LockAsync())
        {
            _disposing = true;
            await ShutdownSchedulers();
        }
    }

    //

    private async Task CreateSchedulers()
    {
        if (_disposing)
        {
            throw new JobManagerException("JobManager is shutting down");
        }

        var schedulerTypes = Enum.GetValues<SchedulerGroup>();
        foreach (var schedulerType in schedulerTypes)
        {
            await CreateScheduler(schedulerType);
        }
    }

    //

    private async Task StartSchedulers()
    {
        if (_disposing)
        {
            throw new JobManagerException("JobManager is shutting down");
        }

        var schedulerNames = _schedulers.Keys;
        foreach (var name in schedulerNames)
        {
            var scheduler = _schedulers[name];
            await scheduler.Start();
        }
    }

    //

    private async Task ShutdownSchedulers()
    {
        var schedulerNames = _schedulers.Keys;
        foreach (var name in schedulerNames)
        {
            var scheduler = _schedulers[name];
            await scheduler.Shutdown(true);
            _schedulers.Remove(name);
        }
    }

    //

    private IScheduler? GetScheduler(SchedulerGroup schedulerType)
    {
        var schedulerName = schedulerType.ToString();
        if (_disposing)
        {
            throw new JobManagerException("JobManager is shutting down");
        }

        return _schedulers.GetValueOrDefault(schedulerName);
    }

    //

    private async Task<IScheduler> CreateScheduler(SchedulerGroup schedulerType)
    {
        var scheduler = GetScheduler(schedulerType);
        if (scheduler != null)
        {
            throw new JobManagerException($"Scheduler already exists: ${schedulerType}" );
        }

        var schedulerName = schedulerType.ToString();

        Directory.CreateDirectory(config.DatabaseDirectory);

        var databaseFile = $"{schedulerName}.db";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(config.DatabaseDirectory, databaseFile),
            Pooling = config.ConnectionPooling,
        }.ToString();

        QuartzSqlite.CreateSchema(connectionString);

        // https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html
        var properties = new NameValueCollection()
        {
            [$"quartz.scheduler.instanceName"] = schedulerName,
            [$"quartz.serializer.type"] = "json",
            [$"quartz.jobStore.useProperties"] = "true",
            [$"quartz.jobStore.dataSource"] = schedulerName,
            [$"quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            [$"quartz.dataSource.{schedulerName}.connectionString"] = connectionString,
            [$"quartz.dataSource.{schedulerName}.provider"] = "SQLite-Microsoft",
            [$"quartz.threadPool.threadCount"] = $"{config.SchedulerThreadCount}",
        };

        var factory = new StdSchedulerFactory(properties);
        scheduler = await factory.GetScheduler();
        scheduler.JobFactory = jobFactory;
        scheduler.ListenerManager.AddJobListener(jobListener, GroupMatcher<JobKey>.AnyGroup());

        _schedulers[schedulerName] = scheduler;

        return scheduler;
    }
}

public class JobManagerException(string message) : OdinSystemException(message);
