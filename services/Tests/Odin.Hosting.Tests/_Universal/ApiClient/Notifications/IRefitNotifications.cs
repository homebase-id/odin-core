using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Time;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications
{
    public interface IRefitNotifications
    {
        private const string RootPath = "/notify/list";

        [Post(RootPath)]
        Task<ApiResponse<HttpContent>> AddNotification([Body] AddNotificationRequest request);

        [Get(RootPath)]
        Task<ApiResponse<NotificationsListResult>> GetList([Query] int count, [Query] UnixTimeUtcUnique? cursor);

        [Put(RootPath)]
        Task<ApiResponse<HttpContent>> Update([Body] UpdateNotificationListRequest request);

        [Delete(RootPath)]
        Task<ApiResponse<HttpContent>> DeleteNotification([Body] DeleteNotificationsRequest request);
    }
}