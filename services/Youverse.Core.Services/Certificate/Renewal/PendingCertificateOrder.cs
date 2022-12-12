using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Certificate.Renewal
{
    /// <summary>
    /// Indicates a certificate that has been ordered as is awaiting validation from the Cert Authority
    /// </summary>
    public class PendingCertificateOrder
    {
        public PendingCertificateOrder()
        {
            Status = CertificateOrderStatus.AwaitingOrderPlacement;
        }
        
        public DotYouIdentity Domain { get; set; }

        public string LocationUri { get; set; }
        public CertificateOrderStatus Status { get; set; }
        
        public CertificateOrder CertificateOrder { get; set; }
        
        public string AccountPem { get; set; }
        
        public Guid RegistryId { get; set; }
    }
}