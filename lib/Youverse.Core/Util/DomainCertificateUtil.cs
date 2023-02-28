using System;
using System.Security.Cryptography.X509Certificates;
using Dawn;
using Youverse.Core.Identity;

namespace Youverse.Core.Util
{
    public class DomainCertificateUtil
    {
        private readonly string _certData;

        /// <summary>
        /// A base64 string of the certificate data
        /// </summary>
        /// <param name="publicKeyCertificate"></param>
        public DomainCertificateUtil(string publicKeyCertificate)
        {
            Guard.Argument(publicKeyCertificate, nameof(publicKeyCertificate)).NotEmpty();

            _certData = publicKeyCertificate;
            ParseCertificate();
        }

        public OdinId OdinId { get; set; }

        private void ParseCertificate()
        {
            using var cert = new X509Certificate2(Convert.FromBase64String(_certData));
            this.OdinId = (OdinId) CertificateUtils.GetDomainFromCommonName(cert.Subject);
        }
    }
}