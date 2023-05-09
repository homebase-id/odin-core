// using System.Threading.Tasks;
//
// namespace Youverse.Core.Services.Certificate.Renewal
// {
//     /// <summary>
//     /// Handles the generation of SSL Certificates for a tenant
//     /// </summary>
//     public interface ITenantCertificateRenewalService
//     {
//         /// <summary>
//         /// Generates the certificate(s) if ready from the CA.
//         /// </summary>
//         /// <returns></returns>
//         Task<CertificateOrderStatus> GenerateCertificateIfReady();
//
//         /// <summary>
//         /// Checks if all certificates are valid or expiring soon; initiates renewal/creation as required
//         /// </summary>
//         /// <returns></returns>
//         Task EnsureCertificatesAreValid(bool force = false);
//
//         /// <summary>
//         /// Gets the response required to validate we control the domain
//         /// </summary>
//         string GetAuthResponse(string token);
//     }
// }