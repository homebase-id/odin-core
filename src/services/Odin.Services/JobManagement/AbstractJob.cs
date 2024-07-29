using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite.ServerDatabase;

namespace Odin.Services.JobManagement;

#nullable enable

public abstract class AbstractJob
{
    // Implement this method to run the job
    public abstract Task<JobExecutionResult> Run(CancellationToken cancellationToken);
    
    // Implement this method to serialize job data to the database
    public abstract string? SerializeJobData();
    
    // Implement this method to deserialize job data from the database
    public abstract void DeserializeJobData(string json);
    
    // Overwrite this property to set the name of the job.
    public virtual string Name => GetType().Name;
    
    // Job Id
    public Guid? Id => Record == null || Record.id == Guid.Empty ? null : Record.id; 

    // Job state
    public JobState State => (JobState?)Record?.state ?? JobState.Unknown;
    
    // Last error
    public string? LastError => Record?.lastError;
    
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
    
    public static AbstractJob CreateInstance(IServiceProvider serviceProvider, JobsRecord record)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.jobType))
        {
            throw new ArgumentException("Job type cannot be null or empty.", nameof(record.jobType));
        }

        var type = Type.GetType(record.jobType);
        if (type == null)
        {
            throw new OdinSystemException($"Unable to find job type {record.jobType}");
        }
        
        if (ActivatorUtilities.CreateInstance(serviceProvider, type) is not AbstractJob job)
        {
            throw new OdinSystemException($"Unable to create instance of job type {type}");
        }
        
        job.Record = record;

        if (!string.IsNullOrEmpty(record.jobData))
        {
            job.DeserializeJobData(record.jobData);            
        }
        
        return job;
    }
    
    //
    
    public static T CreateInstance<T>(IServiceProvider serviceProvider, JobsRecord record) where T : AbstractJob
    {
        if (CreateInstance(serviceProvider, record) is not T job)
        {
            throw new OdinSystemException($"Unable to create instance of job type {typeof(T)}");
        }
        return job;
    }
    
    //
    
}

//

