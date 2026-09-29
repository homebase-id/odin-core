using System;
using System.Net;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Base;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

/// <summary>
/// On the target of a payload move: a payload that is not here yet may simply not have arrived. Reading it
/// is then a 404 with Retry-After that no cache keeps (not the server error a lost payload is), for as long as
/// the identity's transfer is unfinished.
/// </summary>
public class PayloadMoveArrivals(TableJobs tableJobs, TenantContext tenantContext)
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>Call when a payload or thumbnail the header names is missing.</summary>
    public async Task AssertNotStillArrivingAsync()
    {
        var job = await tableJobs.GetJobByHashAsync(PayloadMoveJob.JobHashFor(tenantContext.DotYouRegistryId));
        if (job != null && (JobState)job.state is JobState.Scheduled or JobState.Preflight or JobState.Running)
        {
            throw new OdinRetryLaterException("This payload is still being moved to this host", HttpStatusCode.NotFound, RetryAfter);
        }
    }
}
