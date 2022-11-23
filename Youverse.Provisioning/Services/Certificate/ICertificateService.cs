using Youverse.Core.Services.Registry;

namespace Youverse.Provisioning.Services.Certificate
{
    /// <summary>
    /// Handles the generation of SSL Certificates
    /// </summary>
    public interface ICertificateService
    {
        /// <summary>
        /// Orders a new certificate.  Returns an Id which can be used to check the status of certificate generation.
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public Task<Guid> PlaceOrder(CertificateOrder order);

        /// <summary>
        /// Checks if a certificate is ready.
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public Task<bool> IsCertificateIsReady(Guid orderId);

        /// <summary>
        /// Gets the status for the specified order;
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public Task<CertificateOrderStatus> GetCertificateOrderStatus(Guid orderId);

        /// <summary>
        /// Retrieves the certificate
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public Task<CertificatePemContent> GenerateCertificate(Guid orderId);
    }
}