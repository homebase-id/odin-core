using System;

namespace Odin.Services.JobManagement;

#nullable enable

public enum RunResult
{
    Unknown,    // Invalid state.
    Success,    // Job completed successfully.
    Fail,       // Job failed. Retry the job after RetryInterval has passed or give up if the job has been retried too many times.
    Reschedule, // Reschedule the job (job data is kept).
    Abort,      // Abort and delete the job.
}

public sealed class JobExecutionResult
{
    public RunResult Result { get; private set; }
    public DateTimeOffset RescheduleAt { get; private set; } 
    
    //
   
    public static JobExecutionResult Success() => new JobExecutionResult
    {
        Result = RunResult.Success 
    };
    
    //
    
    public static JobExecutionResult Fail() => new JobExecutionResult
    {
        Result = RunResult.Fail
    };
    
    //
    
    public static JobExecutionResult Reschedule(DateTimeOffset nextRun) => new JobExecutionResult
    {
        Result = RunResult.Reschedule,
        RescheduleAt = nextRun
    };
    
    //

    public static JobExecutionResult Abort() => new JobExecutionResult
    {
        Result = RunResult.Abort
    };
    
    //
    
    private JobExecutionResult()
    {
    }
    
    //
}

