using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Workers.Certificate
{
    /// <summary>
    /// Completes pending certificates which have been validated
    /// </summary>
    [DisallowConcurrentExecution]
    public class ProcessPendingCertificatesJob : IJob
    {
        private readonly ILogger<EnsureIdentityHasValidCertificateJob> _logger;
        private readonly PendingCertificateOrderListService _certificateOrderList;

        public ProcessPendingCertificatesJob(ILogger<EnsureIdentityHasValidCertificateJob> logger, PendingCertificateOrderListService certificateOrderList)
        {
            _logger = logger;
            _certificateOrderList = certificateOrderList;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            //TODO: this is only getting one identity at a time. needs to be upgraded
            Dictionary<Guid, CertificateOrderStatus> statusMap = new();
            var (identities, marker) = await _certificateOrderList.GetIdentities();
            foreach (var ident in identities)
            {
                var status = await GenerateCertificate(ident);
                statusMap.Add(ident.ToHashId(), status);
            }

            if (statusMap.Values.ToList().TrueForAll(s => s == CertificateOrderStatus.CertificateUpdateComplete))
            {
                _certificateOrderList.MarkComplete(marker);
            }
            else
            {
                _certificateOrderList.MarkFailure(marker);
            }
        }

        private async Task<CertificateOrderStatus> GenerateCertificate(OdinId identity)
        {
            _logger.LogInformation($"Checking certificate creation status for {identity.DomainName}");

            var svc = SystemHttpClient.CreateHttp<ICertificateStatusHttpClient>(identity);
            var response = await svc.CheckCertificateCreationStatus();
            if (response.IsSuccessStatusCode)
            {
                return response.Content;
            }

            //TODO: need to log an error here and notify sys admins?
            _logger.LogWarning($"Failed to ensure valid certificate for [{identity}].");
            return CertificateOrderStatus.UnknownServerError;
        }
    }
}