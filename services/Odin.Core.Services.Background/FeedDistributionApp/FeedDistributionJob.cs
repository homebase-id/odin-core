using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.DataSubscription;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Quartz;

namespace Odin.Core.Services.Background.FeedDistributionApp
{
    /// <summary />
    [DisallowConcurrentExecution]
    public class FeedDistributionJob
    {
        private readonly YouverseConfiguration _config;
        private readonly ISystemHttpClient _systemHttpClient;

        public FeedDistributionJob(YouverseConfiguration config, ISystemHttpClient systemHttpClient)
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
            
            throw new YouverseSystemException($"Record type {record.type} not handled");
        }

        private async Task<bool> DistributeFeedFileMetadata(FeedDistributionInfo info)
        {
            var svc = _systemHttpClient.CreateHttps<IFeedDistributionCronClient>(info.OdinId);
            var response = await svc.DistributeFiles();
            return response.IsSuccessStatusCode;
        }
    }
}