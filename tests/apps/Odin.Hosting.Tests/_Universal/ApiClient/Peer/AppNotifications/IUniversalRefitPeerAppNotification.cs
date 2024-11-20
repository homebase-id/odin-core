using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Peer.AppNotification;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer.AppNotifications
{
    public interface IUniversalRefitPeerAppNotification
    {
        private const string RootEndpoint = "/notify/peer";
            
        [Post(RootEndpoint + "/token")]
        Task<ApiResponse<AppNotificationTokenResponse>> GetRemoteNotificationToken([Body]GetRemoteTokenRequest request);

        [Post(RootEndpoint + "/subscriptions/push-notification")]
        Task<ApiResponse<HttpContent>> SubscribePeer([Body]PeerNotificationSubscription request);

        [Delete(RootEndpoint + "/subscriptions/push-notification")]
        Task<ApiResponse<HttpContent>> UnsubscribePeer([Body]PeerNotificationSubscription request);

    }
}