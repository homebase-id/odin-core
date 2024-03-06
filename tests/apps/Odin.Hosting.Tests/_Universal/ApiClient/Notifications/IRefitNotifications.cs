using System;
using System.Net.Http;
using System.Threading.Tasks;
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
        Task<ApiResponse<NotificationsListResult>> GetList([Query] int count, [Query] Int64? cursor);

        [Put(RootPath)]
        Task<ApiResponse<HttpContent>> Update([Body] UpdateNotificationListRequest request);

        [Delete(RootPath)]
        Task<ApiResponse<HttpContent>> DeleteNotification([Body] DeleteNotificationsRequest request);
    }
}