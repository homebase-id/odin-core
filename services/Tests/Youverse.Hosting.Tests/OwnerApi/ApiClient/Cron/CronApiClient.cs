using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Workers.FeedDistributionApp;
using Youverse.Hosting.Tests.OwnerApi.DataSubscription.Follower;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient.Cron;

public class CronApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public CronApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task DistributeFeedFiles()
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RestService.For<IFeedDistributionCronClient>(client);
            client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var resp = await transitSvc.DistributeFiles();
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessTransitOutbox(int batchSize = 1)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var resp = await transitSvc.ProcessOutbox(batchSize);
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }
}