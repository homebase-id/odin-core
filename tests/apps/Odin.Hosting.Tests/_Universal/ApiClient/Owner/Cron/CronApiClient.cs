using System;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Background.DefaultCron;
using Odin.Core.Services.Background.FeedDistributionApp;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Quartz;
using Quartz.Impl;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.Cron;

public class CronApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;
    private readonly Guid _systemApiKey;

    public CronApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
        _systemApiKey = ownerApi.SystemProcessApiKey;
    }

    public async Task<ApiResponse<bool>> DistributeFeedFiles()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RestService.For<IFeedDistributionCronClient>(client);
            client.DefaultRequestHeaders.Add(SystemAuthConstants.Header, _systemApiKey.ToString());
            var resp = await transitSvc.DistributeFiles();
            return resp;
        }
    }

    public async Task<ApiResponse<HttpContent>> ProcessTransitOutbox()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RestService.For<ICronHttpClient>(client);
            client.DefaultRequestHeaders.Add(SystemAuthConstants.Header, _systemApiKey.ToString());
            var resp = await transitSvc.ProcessOutbox();
            return resp;
        }
    }

    public async Task<ApiResponse<HttpContent>> ProcessIncomingPushNotifications()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<ICronHttpClient>(client);
            client.DefaultRequestHeaders.Add(SystemAuthConstants.Header, _systemApiKey.ToString());
            var resp = await svc.ProcessPushNotifications();
            return resp;
        }
    }
}