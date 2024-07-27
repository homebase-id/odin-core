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
using Quartz.Util;

namespace Odin.Services.JobManagement;

#nullable enable

// SEB:TODO
// - db row locking

public interface IJobManager
{
    Task<long> CountJobsAsync();
    Task<bool> DeleteJobAsync(Guid jobId);
    Task<T?> GetJobAsync<T>(Guid jobId) where T : AbstractJob;
    Task<bool> JobExistsAsync(Guid jobId);
    Task<Guid> ScheduleJobAsync(AbstractJob job, JobSchedule? schedule = null);
    Task ExecuteJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task ExecuteJobAsync(AbstractJob job, CancellationToken cancellationToken);
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
    
    public Task ExecuteJobAsync(AbstractJob job, CancellationToken cancellationToken)
    {
        if (job.Record == null)
        {
            throw new OdinSystemException("Job record is null");
        }
        return ExecuteJobAsync(job.Record.id, cancellationToken);
    }
    
    //
    
    // SEB:NOTE
    // This method attempts to run the job immediately. It does not respect the job's run-at schedule.
    // You should only call this directly when testing the job.
    public async Task ExecuteJobAsync(Guid jobId, CancellationToken cancellationToken)
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

        var runResult = RunResult.Unknown;
        string? errorMessage = null;
        var record = OdinSystemSerializer.SlowDeepCloneObject(job.Record)!;
        try
        {
            logger.LogInformation("JobManager starting job {jobId}", jobId);

            // SEB:TODO record locking

            record.state = (int)JobState.Running;
            record.runCount++;
            record.lastRun = UnixTimeUtc.Now();
            await UpsertAsync(record);

            runResult = await job.Run(cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            runResult = RunResult.Reset;
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            runResult = RunResult.Fail;
            errorMessage = ex.Message;
        }

        if (runResult == RunResult.Success)
        {
            logger.LogInformation("JobManager completed job {jobId} ({name}) successfully", 
                record.id, record.name);
            record.state = (int)JobState.Succeeded;
            record.lastError = null;
            record.jobData = job.SerializeJobData();
            await UpsertAsync(record);
        }
        else if (runResult == RunResult.Reset)
        {
            logger.LogInformation("JobManager rescheduled job {jobId} ({name}) for execution ASAP", 
                record.id, record.name);
            record.state = (int)JobState.Scheduled;
            record.runCount = 0;
            record.lastError = errorMessage ?? "job was reset";
            record.jobData = job.SerializeJobData();
            await UpsertAsync(record);
        }
        else if (runResult == RunResult.Abort)
        {
            logger.LogInformation("JobManager deleted job {jobId} ({name})", 
                record.id, record.name);
            await DeleteAsync(record);
        }
        else if (runResult == RunResult.Fail)
        {
            record.lastError = errorMessage ?? "unspecified error";
            record.jobData = job.SerializeJobData();
            if (record.runCount < record.maxAttempts)
            {
                var runAt = DateTimeOffset.Now + TimeSpan.FromMilliseconds(record.retryInterval);
                logger.LogWarning(
                    "JobManager rescheduling unsuccessful job {jobId} ({name}) [{attempt}/{maxAttempt}] for {runat}, Error: {errorMessage}",
                    record.id, record.name, record.runCount, record.maxAttempts, runAt, errorMessage);
                record.state = (int)JobState.Scheduled;
                record.nextRun = runAt.ToUnixTimeMilliseconds();
            }
            else
            {
                logger.LogError(
                    "JobManager giving up on unsuccessful job {jobId} ({name}) after {attempts} attempts. Error: {errorMessage}",
                    record.id, record.name, record.runCount, errorMessage);
                record.state = (int)JobState.Failed;
            }
            await UpsertAsync(record);
        }
        else
        {
            throw new OdinSystemException($"Invalid run result {runResult}. Did you forget to set it?");
        }
    }
    
    //

    public async Task<Guid> ScheduleJobAsync(AbstractJob job, JobSchedule? schedule = null)
    {
        logger.LogDebug("JobManager scheduling job");
        
        var jobId = Guid.NewGuid();
        
        schedule ??= new JobSchedule();
        
        var record = new JobsRecord
        {
            id = jobId,
            name = job.Name,
            state = (int)JobState.Scheduled,
            priority = int.MaxValue / 2,
            nextRun = Math.Max(schedule.RunAt.ToUnixTimeMilliseconds(), DateTimeOffset.Now.ToUnixTimeMilliseconds()),
            lastRun = null,
            runCount = 0,
            maxAttempts = Math.Max(1, schedule.MaxAttempts),
            retryInterval = Math.Max(0, schedule.RetryInterval.Milliseconds),
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

    public Task<JobResult> GetResultAsync(Guid jobId)
    {
        // SEB:TODO load record from tblJobs
        // serverSystemStorage ...
        
        var json = ""; // SEB:TODO get json from record
        var result = JobResult.Deserialize(json);

        return Task.FromResult(result);
    }
    
    //

    public Task<(JobResult, T?)> GetResultAsync<T>(Guid jobId) where T : class
    {
        // SEB:TODO load record from tblJobs
        // serverSystemStorage ...
        
        var json = ""; // SEB:TODO get json from record
        var result = JobResult.Deserialize<T>(json);
        
        return Task.FromResult(result);
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