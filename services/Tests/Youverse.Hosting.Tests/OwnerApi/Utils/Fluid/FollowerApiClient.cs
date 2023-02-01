using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Tests.OwnerApi.Follower;

namespace Youverse.Hosting.Tests.OwnerApi.Utils.Fluid;

public class FollowerApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public FollowerApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<ApiResponse<HttpContent>> FollowIdentity(TestIdentity identity, FollowerNotificationType notificationType, List<TargetDrive> channels, bool assertSuccessStatus = true)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);

            var request = new FollowRequest()
            {
                DotYouId = identity.DotYouId,
                NotificationType = notificationType,
                Channels = channels
            };

            var apiResponse = await svc.Follow(request);
            if (assertSuccessStatus)
            {
                Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to follow identity: [{identity.DotYouId}]");
            }

            return apiResponse;
        }
    }

    public async Task UnfollowIdentity(TestIdentity identity)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);

            var request = new UnfollowRequest()
            {
                DotYouId = identity.DotYouId,
            };

            var apiResponse = await svc.Unfollow(request);
            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to unfollow identity: [{identity.DotYouId}]");
        }
    }

    public async Task<CursoredResult<string>> GetIdentitiesIFollow(string cursor)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetIdentitiesIFollow(cursor);

            Assert.IsTrue(apiResponse.IsSuccessStatusCode);
            Assert.IsNotNull(apiResponse.Content);

            return apiResponse.Content;
        }
    }

    public async Task<CursoredResult<string>> GetIdentitiesFollowingMe(string cursor)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetIdentitiesFollowingMe(cursor);

            Assert.IsTrue(apiResponse.IsSuccessStatusCode);
            Assert.IsNotNull(apiResponse.Content);

            return apiResponse.Content;
        }
    }

    public async Task<FollowerDefinition> GetFollower(TestIdentity identity)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetFollower(identity.DotYouId);

            Assert.IsTrue(apiResponse.IsSuccessStatusCode);
            return apiResponse.Content;
        }
    }
    
    public async Task<FollowerDefinition> GetIdentityIFollow(TestIdentity identity)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ITestFollowerOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await svc.GetIdentityIFollow(identity.DotYouId);

            Assert.IsTrue(apiResponse.IsSuccessStatusCode);
            return apiResponse.Content;
        }
    }
}