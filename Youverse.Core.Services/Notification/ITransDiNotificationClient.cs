using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Notification
{
    public interface ITransDiNotificationClient
    {
        private const string RootPath = "/api/perimeter/notification";
        
        [Post(RootPath)]
        Task<ApiResponse<NoResultResponse>> Notify([Body] SharedSecretEncryptedNotification notification);
    }
}