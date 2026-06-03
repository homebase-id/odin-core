using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Peer.AppNotification;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDrivePeerNotificationHttpClientApiV2
{
    [Post(UnifiedApiRouteConstants.PeerNotifyRoot + "/token")]
    Task<ApiResponse<AppNotificationTokenResponse>> GetToken([Body] GetRemoteTokenRequest request);

    [Post(UnifiedApiRouteConstants.PeerNotifyRoot + "/subscriptions/push-notification")]
    Task<ApiResponse<HttpContent>> Subscribe([Body] PeerNotificationSubscription request);

    [Delete(UnifiedApiRouteConstants.PeerNotifyRoot + "/subscriptions/push-notification")]
    Task<ApiResponse<HttpContent>> Unsubscribe([Body] PeerNotificationSubscription request);

    [Get(UnifiedApiRouteConstants.PeerNotifyRoot + "/subscriptions/push-notification")]
    Task<ApiResponse<List<PeerNotificationSubscription>>> GetSubscriptions();
}
