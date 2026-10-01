using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Services.AppNotifications.Data;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications;

public class AppNotificationsApiClient
{
    private readonly OdinId _identity;
    private readonly IApiClientFactory _factory;

    //TODO: rename to universal
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

    public async Task<ApiResponse<NotificationsCountResult>> GetUnreadCounts()
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.GetUnreadCounts();
            return response;
        }
    }
    public async Task<ApiResponse<NotificationsListResult>> GetList(int count, string cursor = null)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.GetList(count, cursor);
            return response;
        }
    }

    /// <summary>
    /// Polls the notification list until an entry matches, or returns null at the timeout and
    /// prints what the list held so the failing assertion can say what it saw.
    /// </summary>
    public async Task<AppNotification> WaitForNotification(Func<AppNotification, bool> match, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        List<AppNotification> list;
        do
        {
            var response = await GetList(1000);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"notification list for {_identity} failed: {response.StatusCode}");
            }

            list = response.Content?.Results ?? new List<AppNotification>();
            var hit = list.FirstOrDefault(match);
            if (hit != null)
            {
                return hit;
            }

            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        Console.WriteLine($"notification list for {_identity} at timeout: {Describe(list)}");
        return null;
    }

    public async Task<ApiResponse<HttpContent>> SubscribeFirebase(PushNotificationSubscribeFirebaseRequest request)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IRefitPushSubscription>(client, sharedSecret);
        return await svc.SubscribeFirebase(request);
    }

    public async Task<ApiResponse<RedactedPushNotificationSubscription>> GetSubscription()
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IRefitPushSubscription>(client, sharedSecret);
        return await svc.GetSubscription();
    }

    /// <summary>
    /// Removes every device subscription. A test that registers one must call this when done:
    /// any later push to this identity would otherwise try to reach a push relay the test host
    /// does not have and log an error, failing an unrelated fixture's log assertion.
    /// </summary>
    public async Task<ApiResponse<HttpContent>> UnsubscribeAll()
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IRefitPushSubscription>(client, sharedSecret);
        return await svc.UnsubscribeAll();
    }

    public static string Describe(IEnumerable<AppNotification> list) =>
        string.Join(" | ", list.Select(n => $"{n.SenderId} type={n.Options?.TypeId} tag={n.Options?.TagId} app={n.Options?.AppId}"));

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

    public async Task<ApiResponse<HttpContent>> MarkReadByAppId(Guid appId)
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.MarkReadByAppId(appId);
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