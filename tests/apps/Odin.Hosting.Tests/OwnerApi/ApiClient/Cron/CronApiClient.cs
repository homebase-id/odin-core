using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Background.DefaultCron;
using Odin.Services.Background.FeedDistributionApp;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Quartz;
using Quartz.Impl;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Cron;

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

    public async Task DistributeFeedFiles()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RestService.For<IFeedDistributionCronClient>(client);
            client.DefaultRequestHeaders.Add(SystemAuthConstants.Header, _systemApiKey.ToString());
            var resp = await transitSvc.DistributeFiles();
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

}