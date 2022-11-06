namespace Youverse.Provisioning.Services.Certificate
{
    public class PendingCertificateOrder
    {
        public PendingCertificateOrder()
        {
            Status = CertificateOrderStatus.AwaitingOrderPlacement;
        }

        public string OrderUri { get; set; }
        public CertificateOrderStatus Status { get; set; }
        public CertificateOrder CertificateOrder { get; set; }
        
        public string AccountPem { get; set; }
    }
}