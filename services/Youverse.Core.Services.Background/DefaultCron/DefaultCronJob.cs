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
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Workers.FeedDistributionApp;
using Youverse.Core.Storage.Sqlite.ServerDatabase;

namespace Youverse.Core.Services.Workers.DefaultCron
{
    /// <summary />
    [DisallowConcurrentExecution]
    public class DefaultCronJob : IJob
    {
        private readonly ServerSystemStorage _serverSystemStorage;
        private readonly YouverseConfiguration _config;

        public DefaultCronJob(ServerSystemStorage serverSystemStorage, YouverseConfiguration config)
        {
            _serverSystemStorage = serverSystemStorage;
            _config = config;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            int batchSize = _config.Quartz.CronBatchSize;
            if (batchSize <= 0)
            {
                throw new YouverseSystemException("Quartz:CronBatchSize must be greater than zero");
            }
            
            var batch = _serverSystemStorage.tblCron.Pop(batchSize);
            var tasks = new List<Task<(CronRecord record, bool success)>>(batch.Select(ProcessRecord));
            _serverSystemStorage.tblCron.PopCommitList(tasks.Where(t => t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList());
            _serverSystemStorage.tblCron.PopCancelList(tasks.Where(t => !t.Result.success).Select(t => t.Result.record.popStamp.GetValueOrDefault()).ToList());
            await Task.CompletedTask;
        }

        private async Task<(CronRecord record, bool success)> ProcessRecord(CronRecord record)
        {
            bool success = false;
            if (record.type == (Int32)CronJobType.PendingTransitTransfer)
            {
                var identity = (OdinId)record.data.ToStringFromUtf8Bytes();
                success = await StokeOutbox(identity);
            }

            if (record.type == (Int32)CronJobType.GenerateCertificate)
            {
                var identity = (OdinId)record.data.ToStringFromUtf8Bytes();
                var status = await GenerateCertificate(identity);
                success = status == CertificateOrderStatus.CertificateUpdateComplete;
            }

            if (record.type == (Int32)CronJobType.FeedDistribution)
            {
                var job = new FeedDistributionJob(_config);
                success = await job.Execute(record);
            }

            return (record, success);
        }

        private async Task<bool> StokeOutbox(OdinId identity)
        {
            var svc = SystemHttpClient.CreateHttps<ICronHttpClient>(identity);
            var response = await svc.ProcessOutbox();
            return response.IsSuccessStatusCode;
        }

        private async Task<CertificateOrderStatus> GenerateCertificate(OdinId identity)
        {
            // _logger.LogInformation($"Checking certificate creation status for {identity.DomainName}");

            var svc = SystemHttpClient.CreateHttp<ICertificateStatusHttpClient>(identity);
            var response = await svc.CheckCertificateCreationStatus();
            if (response.IsSuccessStatusCode)
            {
                return response.Content;
            }

            //TODO: need to log an error here and notify sys admins?
            // _logger.LogWarning($"Failed to ensure valid certificate for [{identity}].");
            return CertificateOrderStatus.UnknownServerError;
        }
    }
}