using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Workers.DefaultCron;
using Youverse.Core.Storage.Sqlite.ServerDatabase;

namespace Youverse.Core.Services.Workers.FeedDistributionApp
{
    /// <summary />
    [DisallowConcurrentExecution]
    public class FeedDistributionJob
    {
        public async Task<bool> Execute(CronRecord record)
        {
            if (record.type == (Int32)CronJobType.FeedDistribution)
            {
                var distroTask = DotYouSystemSerializer.Deserialize<FeedDistributionInfo>(record.data.ToStringFromUtf8Bytes());
                return await DistributeFeedItem(distroTask);
            }

            throw new YouverseSystemException($"Record type {record.type} not handled");
        }

        private async Task<bool> DistributeFeedItem(FeedDistributionInfo info)
        {
            var svc = SystemHttpClient.CreateHttps<IFeedDistributionClient>(info.OdinId);
            var response = await svc.DistributeReactionPreviewUpdates();
            return response.IsSuccessStatusCode;
        }
    }
}