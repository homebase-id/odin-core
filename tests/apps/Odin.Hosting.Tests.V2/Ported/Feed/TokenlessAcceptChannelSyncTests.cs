using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.JobManagement;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// An accept with no caller token must not schedule a channel sync that can only fail (#1849).
/// </summary>
/// <remarks>
/// Sam sends a request while Frodo's is already waiting, so the send accepts it -- the same tokenless path
/// an introduction auto-accept takes, and one a test can drive directly.
/// </remarks>
[TestFixture]
public class TokenlessAcceptChannelSyncTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [TestCase(false)]
    [TestCase(true)]
    public async Task TokenlessAccept_SchedulesNoChannelSync(bool samFollowsFrodo)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        if (samFollowsFrodo)
        {
            var follow = await sam.V1.Follower.FollowIdentity(frodo.Identity, FollowerNotificationType.AllNotifications, []);
            Assert.That(follow.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }

        var request = await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        Assert.That(request.IsSuccessStatusCode, Is.True, $"SendConnectionRequest failed: {request.StatusCode}");

        var samSend = await sam.Connections.SendConnectionRequest(frodo.Identity, []);
        Assert.That(samSend.IsSuccessStatusCode, Is.True, $"Sam's send failed: {samSend.StatusCode}");

        var jobManager = Host.Server.Services.GetRequiredService<IJobManager>();
        var syncJobs = (await jobManager.GetAllJobsAsync())
            .Where(j => j.jobType == SyncChannelFilesJob.JobTypeId.ToString());
        Assert.That(syncJobs, Is.Empty, samFollowsFrodo
            ? "followed, but with no token the job could only fail"
            : "not followed, so there is nothing to sync");
    }
}
