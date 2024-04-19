using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<FeedDistributionJob> _logger;
        private readonly OdinConfiguration _config;
        private readonly ISystemHttpClient _systemHttpClient;

        public FeedDistributionJob(ILogger<FeedDistributionJob> logger, OdinConfiguration config, ISystemHttpClient systemHttpClient)
        {
            _logger = logger;
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
            try
            {
                var response = await svc.DistributeFiles();
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException e)
            {
                _logger.LogDebug("Error reconciling inbox/outbox: {identity}. Error: {error}", info.OdinId, e.Message);
            }

            return false;
        }
    }
}