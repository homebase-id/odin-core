using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Follower;

public class OwnerFollowerApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public OwnerFollowerApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<ApiResponse<HttpContent>> FollowIdentity(TestIdentity identity, FollowerNotificationType notificationType, List<TargetDrive> channels, bool assertSuccessStatus = true)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);

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
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);

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
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetIdentitiesIFollow(cursor);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(apiResponse.Content);

            return apiResponse.Content;
        }
    }

    public async Task<CursoredResult<string>> GetIdentitiesFollowingMe(string cursor)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetIdentitiesFollowingMe(cursor);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(apiResponse.Content);

            return apiResponse.Content;
        }
    }

    public async Task<FollowerDefinition> GetFollower(TestIdentity identity)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetFollower(identity.OdinId);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            return apiResponse.Content;
        }
    }
    
    public async Task<FollowerDefinition> GetIdentityIFollow(TestIdentity identity)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetIdentityIFollow(identity.OdinId);

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode);
            return apiResponse.Content;
        }
    }
}