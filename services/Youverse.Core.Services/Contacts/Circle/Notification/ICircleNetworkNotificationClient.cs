using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Contacts.Circle.Notification
{
    public interface ICircleNetworkNotificationClient
    {
        private const string RootPath = "/api/perimeter/notification";
        
        [Post(RootPath)]
        Task<ApiResponse<NoResultResponse>> Notify([Body] SharedSecretEncryptedNotification notification);
    }
}