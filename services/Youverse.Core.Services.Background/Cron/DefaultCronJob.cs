using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Workers.Transit;

namespace Youverse.Core.Services.Workers.Cron
{
    /// <summary/>
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
            int count = 100; //TODO: config
            var items = _serverSystemStorage.tblCron.Pop(count, out var marker);
            Dictionary<Guid, CertificateOrderStatus> statusMap = new();
            var markers = new List<Guid>() { marker };
            
            foreach (var item in items)
            {
                var identity = (OdinId)item.data.ToStringFromUtf8Bytes();
                if (item.type == 1)
                {
                    await StokeOutbox(identity);
                }

                if (item.type == 2)
                {
                    var status = await GenerateCertificate(identity);

                    statusMap.Add(identity.ToHashId(), status);

                    if (statusMap.Values.ToList()
                        .TrueForAll(s => s == CertificateOrderStatus.CertificateUpdateComplete))
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

        private async Task StokeOutbox(OdinId identity)
        {
            // _logger.LogInformation($"Stoke running for {identity}");

            var svc = SystemHttpClient.CreateHttps<IOutboxHttpClient>(identity);
            var response = await svc.ProcessOutbox(batchSize: 1);

            //TODO: needs information to determine if it should stoke again; and when

            if (!response.IsSuccessStatusCode)
            {
                //TODO: need to log an error here and notify sys admins?
                // _logger.LogWarning($"Background stoking for [{identity}] failed.");
            }
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