using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.Background;

namespace Odin.Services.JobManagement;

#nullable enable

public interface IJobManager
{
    T NewJob<T>() where T : AbstractJob;
    Task<Guid> ScheduleJobAsync(AbstractJob job, JobSchedule? schedule = null);
    Task RunJobNowAsync(Guid jobId, CancellationToken cancellationToken);
    Task<long> CountJobsAsync();
    Task<bool> DeleteJobAsync(Guid jobId);
    Task<T?> GetJobAsync<T>(Guid jobId) where T : AbstractJob;
    Task<bool> JobExistsAsync(Guid jobId);
    Task DeleteExpiredJobsAsync();
}

//

public class JobManager(
    ILogger<JobManager> logger,
    ICorrelationContext correlationContext,
    ILifetimeScope lifetimeScope,
    IBackgroundServiceTrigger backgroundServiceTrigger)
    : IJobManager
{

    //

    public T NewJob<T>() where T : AbstractJob
    {
        return lifetimeScope.Resolve<T>();
    }

    //
    
    public async Task<Guid> ScheduleJobAsync(AbstractJob job, JobSchedule? schedule = null)
    {
        var jobId = Guid.NewGuid();
        
        schedule ??= new JobSchedule();

        var record = new JobsRecord
        {
            id = jobId,
            name = job.Name,
            state = (int)JobState.Scheduled,
            priority = schedule.Priority,
            nextRun = Math.Max(schedule.RunAt.ToUnixTimeMilliseconds(), DateTimeOffset.Now.ToUnixTimeMilliseconds()),
            lastRun = null,
            runCount = 0,
            maxAttempts = Math.Max(1, schedule.MaxAttempts),
            retryDelay = Math.Max(0, (long)schedule.RetryDelay.TotalMilliseconds),
            onSuccessDeleteAfter = Math.Max(0, (long)schedule.OnSuccessDeleteAfter.TotalMilliseconds),
            onFailureDeleteAfter = Math.Max(0, (long)schedule.OnFailureDeleteAfter.TotalMilliseconds),
            expiresAt = null,
            correlationId = correlationContext.Id,
            jobType = job.JobType,
            jobData = job.SerializeJobData(),
            jobHash = job.CreateJobHash(),
            lastError = null,
        };

        // JobManager works across different scopes and threads, so we need to isolate the tableJobs instance
        // into its own scope to avoid issues with exising scoped connections down the callstack.
        await using var scope = lifetimeScope.BeginLifetimeScope();
        var tableJobs = scope.Resolve<TableJobs>();

        if (record.jobHash == null)
        {
            logger.LogDebug("JobManager scheduling job '{name}' id:{jobId} for {runat}",
                job.Name, jobId, schedule.RunAt.ToString("O"));
            await tableJobs.InsertAsync(record);
        }
        else
        {
            logger.LogDebug("JobManager scheduling unique job '{name}' id:{jobId} hash:{jobHash} for {runat}",
                job.Name, jobId, record.jobHash, schedule.RunAt.ToString("O"));

            // We give it a few tries to insert the job / lookup the existing job from the unique hash, since
            // a race between many jobs having the same hash, where one of them completes and then being deleted
            // while another job is being scheduled with the same hash, will fail to look it up. In which case
            // we let it retry the insert.

            var inserted = 0;
            var attempt = 0;
            while (inserted == 0 && attempt < 5)
            {
                inserted = await tableJobs.TryInsertAsync(record);
                if (inserted == 0)
                {
                    // Job already exists, lets look it up using the jobHash
                    var existingRecord = await tableJobs.GetJobByHashAsync(record.jobHash);
                    if (existingRecord != null)
                    {
                        logger.LogDebug("JobManager unique job '{name}' id:{NewJobId} hash:{jobHash} already exists, returning existing job id:{OldJobId}",
                            existingRecord.name, jobId, record.jobHash, existingRecord.id);
                        return existingRecord.id;
                    }
                }
                attempt++;
            }
            if (inserted == 0)
            {
                var error = $"Could neither insert nor lookup job '{job.Name}' with hash:{record.jobHash}. Check logs. Good luck.";
                logger.LogError(error);
                throw new JobNotFoundException(error);
            }
        }
        
        // Signal job runner to wake up
        backgroundServiceTrigger.PulseBackgroundProcessor(nameof(JobRunnerBackgroundService));

        return jobId;
    }

    //

    // SEB:NOTE
    // This method attempts to run the job immediately. It does not check the job's schedule.
    // You should only call this directly when testing the job.
    public async Task RunJobNowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var scope = lifetimeScope.BeginLifetimeScope();
        try
        {
            using var job = await GetJobAsync<AbstractJob>(jobId, scope);

            if (job?.Record == null)
            {
                throw new JobManagerException($"Job id:{jobId} not found");
            }

            correlationContext.Id = job.Record.correlationId;
            await ExecuteAsync(job, scope, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "JobManager: {message}", e.Message);
        }
    }

    //

    private async Task ExecuteAsync(AbstractJob job, ILifetimeScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(job.Record);

        //
        // Prepare the job for take-off
        //
        // DO NOT check cancellationToken here. It will orphan the job if we bail at this point!
        //

        JobsRecord? record;
        try
        {
            if (job.State is not (JobState.Scheduled or JobState.Preflight))
            {
                throw new JobManagerException($"Job id:{job.Id} is in wrong state: {job.State}");
            }

            record = OdinSystemSerializer.SlowDeepCloneObject(job.Record)!;
            record.state = (int)JobState.Running;
            record.runCount++;
            record.lastRun = UnixTimeUtc.Now();
            await UpdateAsync(record, scope);
        }
        catch (Exception e)
        {
            throw new JobManagerException($"Error preparing job for take-off id:{job.Id}. Job is probably orphaned. Message: {e.Message}", e);
        }

        //
        // Execute the job
        //

        JobExecutionResult result;
        string? errorMessage = null;
        try
        {
            // DO NOT RELOAD THE JOB AFTER THIS POINT!
            logger.LogInformation("JobManager starting job '{name}' id:{jobId}", record.name, record.id);
            result = await job.Run(cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            // Host is probably terminating. Pick up job next time it starts.
            // We add 3 seconds for good measure, mostly to not confuse the test runner.
            result = JobExecutionResult.Reschedule(DateTimeOffset.Now.AddSeconds(3));
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            result = JobExecutionResult.Fail();
            errorMessage = ex.Message;
        }

        // DO NOT RELOAD THE JOB AFTER THIS POINT!

        //
        // Success?
        //
        if (result.Result == RunResult.Success)
        {
            logger.LogInformation("JobManager completed job '{name}' id:{jobId} successfully",
                record.name, record.id);

            if (record.onSuccessDeleteAfter == 0)
            {
                logger.LogDebug("JobManager deleting successful job '{name}' id:{jobId}", record.name, record.id);
                await DeleteAsync(record, scope);
            }
            else
            {
                record.state = (int)JobState.Succeeded;
                record.expiresAt = UnixTimeUtc.Now().AddMilliseconds(record.onSuccessDeleteAfter);
                record.lastError = null;
                record.jobData = job.SerializeJobData();
                await UpdateAsync(record, scope);
            }
        }

        //
        // Reschedule?
        //
        else if (result.Result == RunResult.Reschedule)
        {
            logger.LogInformation("JobManager rescheduled job '{name}' id:{jobId} for {runat}",
                record.name, record.id, result.RescheduleAt.ToString("O"));
            record.state = (int)JobState.Scheduled;
            record.nextRun = result.RescheduleAt.ToUnixTimeMilliseconds();
            record.runCount = 0;
            record.lastError = errorMessage ?? "job was rescheduled";
            record.jobData = job.SerializeJobData();
            await UpdateAsync(record, scope);
        }

        //
        // Abort?
        //
        else if (result.Result == RunResult.Abort)
        {
            logger.LogInformation("JobManager deleting aborted job '{name}' id:{jobId}", record.name, record.id);
            await DeleteAsync(record, scope);
        }

        //
        // Fail?
        //
        else if (result.Result == RunResult.Fail)
        {
            record.lastError = errorMessage ?? "unspecified error";
            record.jobData = job.SerializeJobData();
            if (record.runCount < record.maxAttempts)
            {
                var runAt = DateTimeOffset.Now + TimeSpan.FromMilliseconds(record.retryDelay);
                logger.LogWarning(
                    "JobManager rescheduling unsuccessful job '{name}' id:{jobId} ({attempt}/{maxAttempt}) for {runat}, Error: {errorMessage}",
                    record.name, record.id, record.runCount, record.maxAttempts, runAt.ToString("O"), record.lastError);
                record.state = (int)JobState.Scheduled;
                record.nextRun = runAt.ToUnixTimeMilliseconds();
                await UpdateAsync(record, scope);
            }
            else
            {
                logger.LogError(
                    "JobManager giving up on unsuccessful job '{name}' id:{jobId} after {attempts} attempts. Error: {errorMessage}",
                    record.name, record.id, record.runCount, record.lastError);
                if (record.onFailureDeleteAfter == 0)
                {
                    logger.LogDebug("JobManager deleting failed job '{name}' id:{jobId}", record.name, record.id);
                    await DeleteAsync(record, scope);
                }
                else
                {
                    record.state = (int)JobState.Failed;
                    record.expiresAt = UnixTimeUtc.Now().AddMilliseconds(record.onFailureDeleteAfter);
                    await UpdateAsync(record, scope);
                }
            }
        }

        //
        // Oh dear...
        //
        else
        {
            throw new JobManagerException($"Invalid run result {result.Result}. Did you forget to set it?");
        }
    }
    
    //

    public Task<T?> GetJobAsync<T>(Guid jobId) where T : AbstractJob
    {
        return GetJobAsync<T>(jobId, lifetimeScope);
    }

    //

    private async Task<T?> GetJobAsync<T>(Guid jobId, ILifetimeScope scope) where T : AbstractJob
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var record = await tableJobs.GetAsync(jobId);
    
        if (record == null!)
        {
            return null;
        }

        var job = AbstractJob.CreateInstance<T>(scope, record);
    
        return job;
    }
    
    //
    
    public Task<long> CountJobsAsync()
    {
        return CountJobsAsync(lifetimeScope);
    }

    private async Task<long> CountJobsAsync(ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var result = await tableJobs.GetCountAsync();
        return result;
    }

    //
    
    public Task<bool> JobExistsAsync(Guid jobId)
    {
        return JobExistsAsync(jobId, lifetimeScope);
    }

    private async Task<bool> JobExistsAsync(Guid jobId, ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var result = await tableJobs.JobIdExistsAsync(jobId);
        return result;
    }

    //

    public Task<bool> DeleteJobAsync(Guid jobId)
    {
        return DeleteJobAsync(jobId, lifetimeScope);
    }

    private async Task<bool> DeleteJobAsync(Guid jobId, ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var result = await tableJobs.DeleteAsync(jobId);
        return result > 0;
    }

    //

    public Task DeleteExpiredJobsAsync()
    {
        return DeleteExpiredJobsAsync(lifetimeScope);
    }

    private async Task DeleteExpiredJobsAsync(ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        await tableJobs.DeleteExpiredJobsAsync();
    }

    //

    private async Task<int> UpdateAsync(JobsRecord record, ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var updated = await tableJobs.UpdateAsync(record);
        backgroundServiceTrigger.PulseBackgroundProcessor(nameof(JobRunnerBackgroundService));
        return updated;
    }

    //

    private async Task<int> UpsertAsync(JobsRecord record, ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var updated = await tableJobs.UpsertAsync(record);
        backgroundServiceTrigger.PulseBackgroundProcessor(nameof(JobRunnerBackgroundService));
        return updated;
    }
    
    //
    
    private async Task<int> DeleteAsync(JobsRecord record, ILifetimeScope scope)
    {
        var tableJobs = scope.Resolve<TableJobs>();
        var deleted = await tableJobs.DeleteAsync(record.id);
        backgroundServiceTrigger.PulseBackgroundProcessor(nameof(JobRunnerBackgroundService));
        return deleted;
    } 
   
    //
    
}

public class JobManagerException : OdinSystemException
{
    public JobManagerException(string message) : base(message)
    {
    }
    public JobManagerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class JobNotFoundException(string message) : JobManagerException(message);