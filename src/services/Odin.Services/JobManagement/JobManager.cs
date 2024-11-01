using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Core.Time;
using Odin.Services.Base;

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
    IServiceProvider serviceProvider,
    ServerSystemStorage serverSystemStorage,
    JobRunnerBackgroundService jobRunnerBackgroundService)
    : IJobManager
{
    private readonly TableJobs _tblJobs = serverSystemStorage.Jobs;
    
    //

    public T NewJob<T>() where T : AbstractJob
    {
        return serviceProvider.GetRequiredService<T>();
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
        
        if (record.jobHash == null)
        {
            logger.LogDebug("JobManager scheduling job {name} id:{jobId} for {runat}",
                job.Name, jobId, schedule.RunAt.ToString("O"));
            await _tblJobs.InsertAsync(record);
        }
        else
        {
            logger.LogDebug("JobManager scheduling unique job {name} id:{jobId} hash:{jobHash} for {runat}",
                job.Name, jobId, record.jobHash, schedule.RunAt.ToString("O"));
            var inserted = await _tblJobs.TryInsertAsync(record);
            if (inserted == 0)
            {
                // Job already exists, lets look it up using the jobHash
                var existingRecord = await _tblJobs.GetJobByHashAsync(record.jobHash);
                if (existingRecord != null)
                {
                    logger.LogDebug("JobManager unique job {name} id:{NewJobId} hash:{jobHash} already exists, returning existing job id:{OldJobId}",
                        existingRecord.name, jobId, record.jobHash, existingRecord.id);
                    return existingRecord.id;
                }
                logger.LogError("Could not find job {name} with hash:{hash}. Did it complete already?", job.Name, record.jobHash);
                throw new OdinSystemException($"Could not find job with hash {record.jobHash}");
            }
        }
        
        // Signal job runner to wake up
        jobRunnerBackgroundService.PulseBackgroundProcessor();

        return jobId;
    }

    //
    
    // SEB:NOTE
    // This method attempts to run the job immediately. It does not check the job's schedule.
    // You should only call this directly when testing the job.
    public async Task RunJobNowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // DO NOT check cancellationToken here. It will orphan the job if we bail at this point!

        var job = await GetJobAsync<AbstractJob>(jobId);
        if (job?.Record == null)
        {
            logger.LogError("Job {jobId} not found", jobId);
            return;
        }

        correlationContext.Id = job.Record.correlationId;

        if (job.State is not (JobState.Scheduled or JobState.Preflight))
        {
            logger.LogError("Job {jobId} is in wrong state: {state}", jobId, job.State);
            return;
        }
        
        //
        // Execute the job
        //

        JobExecutionResult result;
        string? errorMessage = null;
        var record = OdinSystemSerializer.SlowDeepCloneObject(job.Record)!;
        try
        {
            logger.LogInformation("JobManager starting job {jobId} ({name})", jobId, job.Record?.name);
            record.state = (int)JobState.Running;
            record.runCount++;
            record.lastRun = UnixTimeUtc.Now();
            await UpdateAsync(record);

            result = await job.Run(cancellationToken);
            
            // DO NOT RELOAD THE JOB AFTER THIS POINT!
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
            logger.LogInformation("JobManager completed job {jobId} ({name}) successfully", 
                record.id, record.name);

            if (record.onSuccessDeleteAfter == 0)
            {
                await DeleteAsync(record);
            }
            else
            {
                record.state = (int)JobState.Succeeded;
                record.expiresAt = UnixTimeUtc.Now().AddMilliseconds(record.onSuccessDeleteAfter);
                record.lastError = null;
                record.jobData = job.SerializeJobData();
                await UpdateAsync(record);
            }
        }
        
        //
        // Reschedule?
        //
        else if (result.Result == RunResult.Reschedule)
        {
            logger.LogInformation("JobManager rescheduled job {jobId} ({name}) for {runat}", 
                record.id, record.name, result.RescheduleAt.ToString("O"));
            record.state = (int)JobState.Scheduled;
            record.nextRun = result.RescheduleAt.ToUnixTimeMilliseconds();
            record.runCount = 0;
            record.lastError = errorMessage ?? "job was rescheduled";
            record.jobData = job.SerializeJobData();
            await UpdateAsync(record);
        }
        
        //
        // Abort?
        //
        else if (result.Result == RunResult.Abort)
        {
            logger.LogInformation("JobManager deleted job {jobId} ({name})", 
                record.id, record.name);
            await DeleteAsync(record);
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
                    "JobManager rescheduling unsuccessful job {jobId} ({name}) [{attempt}/{maxAttempt}] for {runat}, Error: {errorMessage}",
                    record.id, record.name, record.runCount, record.maxAttempts, runAt.ToString("O"), record.lastError);
                record.state = (int)JobState.Scheduled;
                record.nextRun = runAt.ToUnixTimeMilliseconds();
            }
            else
            {
                logger.LogError(
                    "JobManager giving up on unsuccessful job {jobId} ({name}) after {attempts} attempts. Error: {errorMessage}",
                    record.id, record.name, record.runCount, record.lastError);
                if (record.onFailureDeleteAfter == 0)
                {
                    await DeleteAsync(record);
                }
                else
                {
                    record.state = (int)JobState.Failed;
                    record.expiresAt = UnixTimeUtc.Now().AddMilliseconds(record.onFailureDeleteAfter);
                }
            }
            await UpdateAsync(record);
        }
        
        //
        // Oh dear...
        //
        else
        {
            throw new OdinSystemException($"Invalid run result {result.Result}. Did you forget to set it?");
        }
    }
    
    //

    public async Task<T?> GetJobAsync<T>(Guid jobId) where T : AbstractJob
    {
        var record = await _tblJobs.GetAsync(jobId);
    
        if (record == null)
        {
            return null;
        }

        var job = AbstractJob.CreateInstance<T>(serviceProvider, record);
    
        return job;
    }
    
    //
    
    public async Task<long> CountJobsAsync()
    {
        var result = await _tblJobs.GetCountAsync();
        return result;
    }
    
    //
    
    public async Task<bool> JobExistsAsync(Guid jobId)
    {
        var result = await _tblJobs.JobIdExistsAsync(jobId);
        return result;
    }
    
    //

    public async Task<bool> DeleteJobAsync(Guid jobId)
    {
        var result = await _tblJobs.DeleteAsync(jobId);
        return result > 0;
    }
    
    //

    public async Task DeleteExpiredJobsAsync()
    {
        await _tblJobs.DeleteExpiredJobsAsync();
    }

    //

    private async Task<int> UpdateAsync(JobsRecord record)
    {
        var updated = await _tblJobs.UpdateAsync(record);
        jobRunnerBackgroundService.PulseBackgroundProcessor();
        return updated;
    }

    //

    private async Task<int> UpsertAsync(JobsRecord record)
    {
        var updated = await _tblJobs.UpsertAsync(record);
        jobRunnerBackgroundService.PulseBackgroundProcessor();       
        return updated;
    }
    
    //
    
    private async Task<int> DeleteAsync(JobsRecord record)
    {
        var deleted = await _tblJobs.DeleteAsync(record.id);
        jobRunnerBackgroundService.PulseBackgroundProcessor();
        return deleted;
    } 
   
    //
    
}