using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Quartz;

namespace Odin.Services.Background.FeedDistributionApp
{
    /// <summary />
    [DisallowConcurrentExecution]
    public class FeedDistributionJob
    {
        private readonly OdinConfiguration _config;
        private readonly ISystemHttpClient _systemHttpClient;

        public FeedDistributionJob(OdinConfiguration config, ISystemHttpClient systemHttpClient)
        {
            _config = config;
            _systemHttpClient = systemHttpClient;
        }

        public async Task<bool> Execute(CronRecord record)
        {
            if (record.type == (Int32)CronJobType.FeedDistribution)
            {
                var distroTask = OdinSystemSerializer.Deserialize<FeedDistributionInfo>(record.data.ToStringFromUtf8Bytes());
                return await DistributeFeedFileMetadata(distroTask);
            }
            
            throw new OdinSystemException($"Record type {record.type} not handled");
        }

        private async Task<bool> DistributeFeedFileMetadata(FeedDistributionInfo info)
        {
            var svc = _systemHttpClient.CreateHttps<IFeedDistributionCronClient>(info.OdinId);
            var response = await svc.DistributeFiles();
            return response.IsSuccessStatusCode;
        }
    }
}