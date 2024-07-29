using System;
using System.Threading;
using System.Threading.Tasks;
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
    Task<Guid> ScheduleJobAsync(AbstractJob job, JobSchedule? schedule = null);
    Task RunJobNowAsync(Guid jobId, CancellationToken cancellationToken);
    Task<long> CountJobsAsync();
    Task<bool> DeleteJobAsync(Guid jobId);
    Task<T?> GetJobAsync<T>(Guid jobId) where T : AbstractJob;
    Task<bool> JobExistsAsync(Guid jobId);
    public void PulseBackgroundProcessor();
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
    
    public async Task<Guid> ScheduleJobAsync(AbstractJob job, JobSchedule? schedule = null)
    {
        var jobId = Guid.NewGuid();
        
        schedule ??= new JobSchedule();
        
        logger.LogDebug("JobManager scheduling job {jobId} ({name}) for {runat}", 
            jobId, job.Name, schedule.RunAt.ToString("O"));
        
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
            retryInterval = Math.Max(0, (long)schedule.RetryInterval.TotalMilliseconds),
            onSuccessDeleteAfter = Math.Max(schedule.OnSuccessDeleteAfter.ToUnixTimeMilliseconds(), DateTimeOffset.Now.ToUnixTimeMilliseconds()),
            onFailureDeleteAfter = Math.Max(schedule.OnFailureDeleteAfter.ToUnixTimeMilliseconds(), DateTimeOffset.Now.ToUnixTimeMilliseconds()),
            correlationId = correlationContext.Id,
            jobType = job.GetType().AssemblyQualifiedName,
            jobData = job.SerializeJobData(),
            jobHash = null,
            lastError = null,
        };
        
        using (var cn = await CreateConnectionAsync())
        {
            // SEB:TODO lookup inputHash

            _tblJobs.Insert(cn, record);
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
        if (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("JobManager job {jobId} cancelled", jobId);
            return;
        }
        
        var job = await GetJobAsync<AbstractJob>(jobId);
        if (job == null)
        {
            logger.LogError("Job {jobId} not found", jobId);
            return;
        }
        
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
            await UpsertAsync(record);

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
            record.state = (int)JobState.Succeeded;
            record.lastError = null;
            record.jobData = job.SerializeJobData();
            await UpsertAsync(record);
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
            await UpsertAsync(record);
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
                var runAt = DateTimeOffset.Now + TimeSpan.FromMilliseconds(record.retryInterval);
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
                record.state = (int)JobState.Failed;
            }
            await UpsertAsync(record);
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
        JobsRecord record;
        using (var cn = await CreateConnectionAsync())
        {
            record = _tblJobs.Get(cn, jobId);
        }
    
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
        using var cn = await CreateConnectionAsync();
        var result = await _tblJobs.GetCountAsync(cn);
        return result;
    }
    
    //
    
    public async Task<bool> JobExistsAsync(Guid jobId)
    {
        using var cn = await CreateConnectionAsync();
        var result = await _tblJobs.JobIdExists(cn, jobId);
        return result;
    }
    
    //

    public async Task<bool> DeleteJobAsync(Guid jobId)
    {
        using var cn = await CreateConnectionAsync();
        var result = _tblJobs.Delete(cn, jobId);
        return result > 0;
    }
    
    //
    
    public void PulseBackgroundProcessor()
    {
        jobRunnerBackgroundService.PulseBackgroundProcessor();
    }
    
    //
    
    private Task<DatabaseConnection> CreateConnectionAsync()
    {
        return Task.FromResult(serverSystemStorage.CreateConnection());
    } 
    
    //
    
    private async Task<int> UpsertAsync(JobsRecord record)
    {
        using var cn = await CreateConnectionAsync();
        var updated = _tblJobs.Upsert(cn, record);
        jobRunnerBackgroundService.PulseBackgroundProcessor();       
        return updated;
    }
    
    //
    
    private async Task<int> DeleteAsync(JobsRecord record)
    {
        using var cn = await CreateConnectionAsync();
        var deleted = _tblJobs.Delete(cn, record.id);
        jobRunnerBackgroundService.PulseBackgroundProcessor();
        return deleted;
    } 
   
    //
    
}