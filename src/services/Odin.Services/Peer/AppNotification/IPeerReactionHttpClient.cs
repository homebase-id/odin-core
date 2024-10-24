using System.Threading.Tasks;
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
    }
}