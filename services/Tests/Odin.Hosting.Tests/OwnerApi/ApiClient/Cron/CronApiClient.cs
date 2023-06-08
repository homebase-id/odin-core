using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Background.DefaultCron;
using Odin.Core.Services.Background.FeedDistributionApp;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Quartz;
using Quartz.Impl;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Cron;

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
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RestService.For<IFeedDistributionCronClient>(client);
            client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var resp = await transitSvc.DistributeFiles();
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessTransitOutbox(int batchSize = 1)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var resp = await transitSvc.ProcessOutbox(batchSize);
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }
    
    public async Task TriggerDefaultCronJob()
    {
        ISchedulerFactory sf = new StdSchedulerFactory();
        var scheduler = sf.GetScheduler().Result;
        await scheduler.Start();
        
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("TriggerDefaultCronJob", "UnitTests")
            .StartNow()
            .WithSimpleSchedule(x => x.WithRepeatCount(1).WithInterval(TimeSpan.MaxValue))
            .Build();
        
        await scheduler.ScheduleJob(new JobDetailImpl(nameof(DefaultCronJob), typeof(DefaultCronJob)), trigger);
        
        // manually trigger the job
        await scheduler.TriggerJob(jobKey: new JobKey(nameof(DefaultCronJob)));
    }
}