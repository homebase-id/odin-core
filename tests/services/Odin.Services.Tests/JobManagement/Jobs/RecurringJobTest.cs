using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class RecurringJobTestData
{
    public int RunCount { get; set; }
    public int OnCompletedCount { get; set; }

    // How many times OnCompletedAsync should reschedule before it stops (returns null).
    public int RepeatCount { get; set; } = 2;

    // When true, every run fails terminally (use with MaxAttempts=1) but the job still recurs.
    public bool AlwaysFail { get; set; }

    // When true, OnCompletedAsync throws — exercises the manager's throw-isolation fallback.
    public bool ThrowInOnCompleted { get; set; }
}

// A job that drives its own recurrence through OnCompletedAsync: it reschedules on both success
// and terminal failure until RepeatCount is reached, then finalizes normally.
public class RecurringJobTest(ILogger<RecurringJobTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("3386eaf2-3c0d-44e4-8b27-84e173b1633e");
    public override string JobType => JobTypeId.ToString();

    public RecurringJobTestData JobData { get; set; } = new();

    //

    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running RecurringJobTest");
        JobData.RunCount++;
        return Task.FromResult(JobData.AlwaysFail
            ? JobExecutionResult.Fail()
            : JobExecutionResult.Success());
    }

    //

    public override Task<DateTimeOffset?> OnCompletedAsync(JobCompletion completion)
    {
        JobData.OnCompletedCount++;

        if (JobData.ThrowInOnCompleted)
        {
            throw new Exception("boom in OnCompleted");
        }

        // Keep recurring until we have rescheduled RepeatCount times, then stop.
        DateTimeOffset? nextRun = JobData.OnCompletedCount <= JobData.RepeatCount
            ? completion.ScheduledFor.AddMilliseconds(200)
            : null;
        return Task.FromResult(nextRun);
    }

    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(JobData);
    }

    //

    public override void DeserializeJobData(string json)
    {
        JobData = OdinSystemSerializer.DeserializeOrThrow<RecurringJobTestData>(json);
    }

    //

    public override string? CreateJobHash()
    {
        var text = JobType;
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
