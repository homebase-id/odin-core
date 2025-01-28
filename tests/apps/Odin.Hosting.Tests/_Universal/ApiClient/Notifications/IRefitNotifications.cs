using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.AppNotifications.Data;
using Odin.Core.Time;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications
{
    public interface IRefitNotifications
    {
        private const string RootPath = "/notify/list";

        [Post(RootPath)]
        Task<ApiResponse<AddNotificationResult>> AddNotification([Body] AddNotificationRequest request);

        [Get(RootPath)]
        Task<ApiResponse<NotificationsListResult>> GetList([Query] int count, [Query] string cursor);

        [Get(RootPath + "/counts-by-appid")]
        Task<ApiResponse<NotificationsCountResult>> GetUnreadCounts();

        [Put(RootPath)]
        Task<ApiResponse<HttpContent>> Update([Body] UpdateNotificationListRequest request);

        [Post(RootPath + "/mark-read-by-appid")]
        Task<ApiResponse<HttpContent>> MarkReadByAppId([Body] Guid appId);

        [Delete(RootPath)]
        Task<ApiResponse<HttpContent>> DeleteNotification([Body] DeleteNotificationsRequest request);
    }
}