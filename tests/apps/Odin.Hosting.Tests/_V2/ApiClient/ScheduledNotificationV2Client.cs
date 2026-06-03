using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.AppNotifications.Push.Scheduled;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class ScheduledNotificationV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<ScheduleNotificationResult>> Schedule(AppNotificationOptions options, UnixTimeUtc sendAt)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IScheduledNotificationHttpClientV2>(client, sharedSecret);
        return await svc.Schedule(new ScheduleNotificationRequest
        {
            Options = options,
            SendAt = sendAt,
        });
    }

    public async Task<ApiResponse<HttpContent>> Cancel(Guid jobId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IScheduledNotificationHttpClientV2>(client, sharedSecret);
        return await svc.Cancel(jobId);
    }
}
