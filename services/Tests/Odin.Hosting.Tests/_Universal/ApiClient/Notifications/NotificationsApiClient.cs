using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications;

public class AppNotificationsApiClient
{
    private readonly OdinId _identity;
    private readonly IApiClientFactory _factory;

    public AppNotificationsApiClient(OdinId identity, IApiClientFactory factory)
    {
        _identity = identity;
        _factory = factory;
    }

    public async Task<ApiResponse<AddNotificationResult>> AddNotification(AppNotificationOptions options)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.AddNotification(new AddNotificationRequest()
            {
                AppNotificationOptions = options
            });

            return response;
        }
    }

    public async Task<ApiResponse<NotificationsListResult>> GetList(int count, Int64? cursor = null)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.GetList(count, cursor);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> Update(List<UpdateNotificationRequest> updates)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.Update(new UpdateNotificationListRequest()
            {
                Updates = updates
            });
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> Delete(List<Guid> idList)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.DeleteNotification(new DeleteNotificationsRequest()
            {
                IdList = idList
            });
            return response;
        }
    }
}