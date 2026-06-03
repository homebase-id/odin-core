using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Peer.AppNotification;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

/// <summary>
/// V2 client for managing subscriptions to live notifications on drives hosted by other identities
/// (<c>/api/v2/peer/notify/...</c>).
/// </summary>
public class PeerNotificationV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<AppNotificationTokenResponse>> GetTokenAsync(OdinId peer)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.GetToken(new GetRemoteTokenRequest { Identity = peer });
    }

    public async Task<ApiResponse<HttpContent>> SubscribeAsync(OdinId peer, Guid subscriptionId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.Subscribe(new PeerNotificationSubscription { Identity = peer, SubscriptionId = subscriptionId });
    }

    public async Task<ApiResponse<HttpContent>> UnsubscribeAsync(OdinId peer, Guid subscriptionId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.Unsubscribe(new PeerNotificationSubscription { Identity = peer, SubscriptionId = subscriptionId });
    }

    public async Task<ApiResponse<List<PeerNotificationSubscription>>> GetSubscriptionsAsync()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.GetSubscriptions();
    }
}
