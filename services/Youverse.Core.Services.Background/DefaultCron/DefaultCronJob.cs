using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Workers.FeedDistributionApp;

namespace Youverse.Core.Services.Workers.DefaultCron
{
    /// <summary />
    [DisallowConcurrentExecution]
    public class DefaultCronJob : IJob
    {
        private readonly ServerSystemStorage _serverSystemStorage;

        public DefaultCronJob(ServerSystemStorage serverSystemStorage)
        {
            _serverSystemStorage = serverSystemStorage;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            int count = 1; //TODO: config
            var items = _serverSystemStorage.tblCron.Pop(count);

            if (!items.Any())
            {
                return;
            }

            Dictionary<Guid, CertificateOrderStatus> statusMap = new();
            
            var markers = new List<Guid>() { items.First().popStamp.GetValueOrDefault() };

            foreach (var item in items)
            {
                if (item.type == (Int32)CronJobType.PendingTransitTransfer)
                {
                    var identity = (OdinId)item.data.ToStringFromUtf8Bytes();
                    var success = await StokeOutbox(identity, batchSize: 1);
                    if (success)
                    {
                        _serverSystemStorage.tblCron.PopCommitList(markers);
                    }
                    else
                    {
                        _serverSystemStorage.tblCron.PopCancelList(markers);
                    }
                }

                if (item.type == (Int32)CronJobType.GenerateCertificate)
                {
                    var identity = (OdinId)item.data.ToStringFromUtf8Bytes();
                    var status = await GenerateCertificate(identity);

                    statusMap.Add(identity.ToHashId(), status);

                    if (statusMap.Values.ToList().TrueForAll(s => s == CertificateOrderStatus.CertificateUpdateComplete))
                    {
                        _serverSystemStorage.tblCron.PopCommitList(markers);
                    }
                    else
                    {
                        _serverSystemStorage.tblCron.PopCancelList(markers);
                    }
                }

                if (item.type == (Int32)CronJobType.FeedDistribution)
                {
                    var job = new FeedDistributionJob();
                    var success = await job.Execute(item);
                    if (success)
                    {
                        _serverSystemStorage.tblCron.PopCommitList(markers);
                    }
                    else
                    {
                        _serverSystemStorage.tblCron.PopCancelList(markers);
                    }
                }
            }
        }

        private async Task<bool> StokeOutbox(OdinId identity, int batchSize)
        {
            var svc = SystemHttpClient.CreateHttps<ICronHttpClient>(identity);
            var response = await svc.ProcessOutbox(batchSize);
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