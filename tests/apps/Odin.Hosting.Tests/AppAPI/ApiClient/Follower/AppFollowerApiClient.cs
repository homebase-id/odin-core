using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Follower;

public class AppFollowerApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppFollowerApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }
    
    public async Task<ApiResponse<HttpContent>> FollowIdentity(TestIdentity identity, FollowerNotificationType notificationType, List<TargetDrive> channels,
        bool assertSuccessStatus = true)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerAppClient>(client, _token.SharedSecret);

            var request = new FollowRequest()
            {
                OdinId = identity.OdinId,
                NotificationType = notificationType,
                Channels = channels
            };

            var apiResponse = await svc.Follow(request);
            if (assertSuccessStatus)
            {
                ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to follow identity: [{identity.OdinId}]");
            }

            return apiResponse;
        }
    }

    public async Task UnfollowIdentity(TestIdentity identity)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerAppClient>(client, _token.SharedSecret);

            var request = new UnfollowRequest()
            {
                OdinId = identity.OdinId,
            };

            var apiResponse = await svc.Unfollow(request);
            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to unfollow identity: [{identity.OdinId}]");
        }
    }

    public async Task<CursoredResult<string>> GetIdentitiesIFollow(string cursor)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerAppClient>(client, _token.SharedSecret);
            var apiResponse = await svc.GetIdentitiesIFollow(cursor);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(apiResponse.Content);

            return apiResponse.Content;
        }
    }

    public async Task<CursoredResult<string>> GetIdentitiesFollowingMe(string cursor)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerAppClient>(client, _token.SharedSecret);
            var apiResponse = await svc.GetIdentitiesFollowingMe(cursor);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(apiResponse.Content);

            return apiResponse.Content;
        }
    }

    public async Task<FollowerDefinition> GetFollower(TestIdentity identity)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerAppClient>(client, _token.SharedSecret);
            var apiResponse = await svc.GetFollower(identity.OdinId);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            return apiResponse.Content;
        }
    }

    public async Task<FollowerDefinition> GetIdentityIFollow(TestIdentity identity)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerAppClient>(client, _token.SharedSecret);
            var apiResponse = await svc.GetIdentityIFollow(identity.OdinId);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            return apiResponse.Content;
        }
    }
}