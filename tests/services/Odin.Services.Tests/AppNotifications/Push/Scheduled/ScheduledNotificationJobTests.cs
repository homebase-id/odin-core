using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Services.AppNotifications.Push.Scheduled;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.AppNotifications.Push.Scheduled;

// OnCompletedAsync is pure scheduling logic with no dependency on the tenant container or push
// pipeline, so it's tested directly here rather than through a full background job run -- the
// underlying reschedule mechanics (same job id, state reset, etc.) are already covered generically
// in JobManagerTests (ItShouldRescheduleSuccessfulJobViaOnCompleted / ...FailedJobViaOnCompleted).
public class ScheduledNotificationJobTests
{
    [Test]
    public async Task OnCompletedAsync_ReturnsNull_WhenNotRecurring()
    {
        var job = new ScheduledNotificationJob(null!, NullLogger<ScheduledNotificationJob>.Instance)
        {
            Data = new ScheduledNotificationJobData { RecurrenceInterval = null }
        };

        var nextRun = await job.OnCompletedAsync(new JobCompletion(RunResult.Success, null, 1, DateTimeOffset.UtcNow));

        Assert.That(nextRun, Is.Null);
    }

    [Test]
    public async Task OnCompletedAsync_AnchorsNextRunOffScheduledFor_WhenRecurring()
    {
        var job = new ScheduledNotificationJob(null!, NullLogger<ScheduledNotificationJob>.Instance)
        {
            Data = new ScheduledNotificationJobData { RecurrenceInterval = 60_000 }
        };

        // ScheduledFor is deliberately in the past, as if the runner was delayed -- the next occurrence
        // must anchor off the intended slot, not wall-clock now, or cadence drifts under load.
        var scheduledFor = DateTimeOffset.UtcNow.AddMinutes(-5);
        var nextRun = await job.OnCompletedAsync(new JobCompletion(RunResult.Success, null, 1, scheduledFor));

        Assert.That(nextRun, Is.EqualTo(scheduledFor.AddMilliseconds(60_000)));
    }

    [Test]
    public async Task OnCompletedAsync_AlsoRecursAfterExhaustedFailure()
    {
        var job = new ScheduledNotificationJob(null!, NullLogger<ScheduledNotificationJob>.Instance)
        {
            Data = new ScheduledNotificationJobData { RecurrenceInterval = 60_000 }
        };

        var scheduledFor = DateTimeOffset.UtcNow;
        var nextRun = await job.OnCompletedAsync(new JobCompletion(RunResult.Fail, "boom", 3, scheduledFor));

        Assert.That(nextRun, Is.EqualTo(scheduledFor.AddMilliseconds(60_000)));
    }
}
