using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Services.JobManagement.Jobs;

#nullable enable

public abstract class AbstractJob : IDisposable
{
    // Implement this method to run the job
    public abstract Task<JobExecutionResult> Run(CancellationToken cancellationToken);
    
    // Implement this method to serialize job data to the database
    public abstract string? SerializeJobData();
    
    // Implement this method to deserialize job data from the database
    public abstract void DeserializeJobData(string json);
    
    // Override this property to set the name of the job.
    public virtual string Name => GetType().Name;
    
    // Job Id
    public Guid? Id => Record == null || Record.id == Guid.Empty ? null : Record.id; 

    // Job state
    public JobState State => (JobState?)Record?.state ?? JobState.Unknown;
    
    // Last error
    public string? LastError => Record?.lastError;

    // JobType
    public abstract string JobType { get; }

    // Override this to create a job hash value. This is used to determine if a job is unique. Two jobs
    // with the same hash cannot exist in the database at the same time. If this method returns null, it means
    // that the job is not unique.
    public virtual string? CreateJobHash()
    {
        return null;
    }
    
    // Override this to tweak the response object used by the API 
    public virtual JobApiResponse CreateApiResponseObject()
    {
        return new JobApiResponse
        {
            JobId = Id,
            State = State,
            Error = LastError,
            Data = SerializeJobData()
        };
    }
    
    // Low level database job record (read-only)
    public JobsRecord? Record { get; private set; }
    
    public static AbstractJob CreateInstance(ILifetimeScope lifetimeScope, JobsRecord record)
    {
        ArgumentNullException.ThrowIfNull(lifetimeScope);
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.jobType))
        {
            throw new ArgumentException("Job type cannot be null or empty.", nameof(record.jobType));
        }

        Type? jobType;
        if (Guid.TryParse(record.jobType, out var jobTypeId))
        {
            var jobTypeRegistry = lifetimeScope.Resolve<IJobTypeRegistry>();
            jobType = jobTypeRegistry.GetJobType(jobTypeId);
        }
        else
        {
            // Fallback to old behavior
            jobType = Type.GetType(record.jobType);
        }

        if (jobType == null)
        {
            throw new OdinSystemException($"Unable to find job type {record.jobType}");
        }

        if (lifetimeScope.Resolve(jobType) is not AbstractJob job)
        {
            throw new OdinSystemException($"Unable to create instance of job type {jobType}");
        }

        job.Record = record;

        if (!string.IsNullOrEmpty(record.jobData))
        {
            job.DeserializeJobData(record.jobData);            
        }
        
        return job;
    }
    
    //
    
    public static T CreateInstance<T>(ILifetimeScope lifetimeScope, JobsRecord record) where T : AbstractJob
    {
        if (CreateInstance(lifetimeScope, record) is not T job)
        {
            throw new OdinSystemException($"Unable to create instance of job type {typeof(T)}");
        }
        return job;
    }
    
    //
    
    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);        
    }
    
    protected virtual void Dispose(bool disposing)
    {
    }    
    
    //
    
}

//

