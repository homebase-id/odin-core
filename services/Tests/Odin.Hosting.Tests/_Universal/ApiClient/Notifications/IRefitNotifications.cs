using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications
{
    public interface IRefitNotifications
    {
        private const string RootPath = "/notify/list";

        [Post(RootPath + "")]
        Task<ApiResponse<HttpContent>> AddNotification([Body] AddNotificationRequest request);

        [Get(RootPath + "/list")]
        Task<ApiResponse<HttpContent>> GetNotificationsList();

        [Get(RootPath + "/{id}")]
        Task<ApiResponse<HttpContent>> GetNotification(Guid id);

        [Put(RootPath + "/{id}")]
        Task<ApiResponse<HttpContent>> Update();

        [Delete(RootPath + "/{id}")]
        Task<ApiResponse<HttpContent>> DeleteNotification([Query] Guid id);
    }
}