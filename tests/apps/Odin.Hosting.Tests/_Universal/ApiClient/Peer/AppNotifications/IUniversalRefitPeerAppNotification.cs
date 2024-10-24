using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Drives;
using Odin.Services.Peer.AppNotification;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer.AppNotifications
{
    public interface IUniversalRefitPeerAppNotification
    {
        private const string RootEndpoint = "/notify/peer";
            
        [Post(RootEndpoint + "/token")]
        Task<ApiResponse<AppNotificationTokenResponse>> GetRemoteNotificationToken([Body]GetRemoteTokenRequest request);

    }
}