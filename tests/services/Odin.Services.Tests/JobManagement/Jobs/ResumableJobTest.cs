using System;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Tests.JobManagement.Jobs;

// A SimpleJobTest that runs while its identity is stopped and is rescheduled when orphaned, as the
// payload move's transfer job does
public class ResumableJobTest(ILogger<SimpleJobTest> logger) : SimpleJobTest(logger)
{
    public new static readonly Guid JobTypeId = Guid.Parse("5b0f3c1e-8f7a-4d0e-9c11-3a2f6e7d9b40");
    public override string JobType => JobTypeId.ToString();

    public override bool RunsWhileIdentityStopped => true;
    public override TimeSpan? RescheduleIfOrphanedAfter => TimeSpan.FromMinutes(30);
}
