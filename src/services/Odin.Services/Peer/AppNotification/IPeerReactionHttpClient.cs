using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Refit;

namespace Odin.Services.Peer.AppNotification
{
    public interface IPeerAppNotificationHttpClient
    {
        private const string RootPath = PeerApiPathConstants.AppNotificationsV1;

        [Post(RootPath + "/token")]
        Task<ApiResponse<SharedSecretEncryptedPayload>> GetAppNotificationToken();
        
        
        [Post(RootPath + "/enqueue-push-notification")]
        Task<ApiResponse<PeerTransferResponse>> EnqueuePushNotification([Body] PushNotificationOutboxRecord request);

    }
}