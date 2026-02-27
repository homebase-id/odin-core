using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Core.Storage.Database.Identity.Table;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Follower;

public class UniversalFollowerApiClient(OdinId targetIdentity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> FollowIdentity(OdinId identity, FollowerNotificationType notificationType, List<TargetDrive> channels = null)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);

            var request = new FollowRequest()
            {
                OdinId = identity,
                NotificationType = notificationType,
                Channels = channels ?? []
            };

            var apiResponse = await svc.Follow(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> UnfollowIdentity(OdinId identity)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var request = new UnfollowRequest()
            {
                OdinId = identity,
            };

            return await svc.Unfollow(request);
        }
    }

    public async Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollow(string cursor)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetIdentitiesIFollow(cursor);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<CursoredResult<string>>> GetIdentitiesFollowingMe(string cursor)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetIdentitiesFollowingMe(cursor);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<FollowerDefinition>> GetFollower(OdinId identity)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetFollower(identity);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<FollowerDefinition>> GetIdentityIFollow(OdinId identity)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetIdentityIFollow(identity);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> SynchronizeFeed(OdinId odinId)
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.SynchronizeFeedHistory(new SynchronizeFeedHistoryRequest()
            {
                OdinId = odinId
            });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<List<MySubscriptionsRecord>>> GetMySubscriptions()
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetMySubscriptions();
            return apiResponse;
        }
    }

    public async Task<ApiResponse<List<MySubscribersRecord>>> GetMySubscribers()
    {
        var client = factory.CreateHttpClient(targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetMySubscribers();
            return apiResponse;
        }
    }
}