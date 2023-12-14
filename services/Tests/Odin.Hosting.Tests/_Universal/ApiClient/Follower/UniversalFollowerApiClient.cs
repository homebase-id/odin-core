using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Follower;

public class UniversalFollowerApiClient
{
    private readonly OdinId _targetIdentity;
    private readonly IApiClientFactory _factory;

    public UniversalFollowerApiClient(OdinId targetIdentity, IApiClientFactory factory)
    {
        _targetIdentity = targetIdentity;
        _factory = factory;
    }
    
    public async Task<ApiResponse<HttpContent>> FollowIdentity(OdinId identity, FollowerNotificationType notificationType, List<TargetDrive> channels)
    {
        var client = _factory.CreateHttpClient(_targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);

            var request = new FollowRequest()
            {
                OdinId = identity,
                NotificationType = notificationType,
                Channels = channels
            };

            var apiResponse = await svc.Follow(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> UnfollowIdentity(OdinId identity)
    {
        var client = _factory.CreateHttpClient(_targetIdentity, out var sharedSecret);
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
        var client = _factory.CreateHttpClient(_targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetIdentitiesIFollow(cursor);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<CursoredResult<string>>> GetIdentitiesFollowingMe(string cursor)
    {
        var client = _factory.CreateHttpClient(_targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetIdentitiesFollowingMe(cursor);
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<FollowerDefinition>> GetFollower(OdinId identity)
    {
        var client = _factory.CreateHttpClient(_targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetFollower(identity);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<FollowerDefinition>> GetIdentityIFollow(OdinId identity)
    {
        var client = _factory.CreateHttpClient(_targetIdentity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalFollowerClient>(client, sharedSecret);
            var apiResponse = await svc.GetIdentityIFollow(identity);
            return apiResponse;
        }
    }
}