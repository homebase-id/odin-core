using System;
using System.Security.Cryptography.X509Certificates;

using Odin.Core.Identity;

namespace Odin.Core.Util
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
            _certData = publicKeyCertificate;
            ParseCertificate();
        }

        public OdinId OdinId { get; set; }

        private void ParseCertificate()
        {
            using var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(_certData));
            OdinId = (OdinId) CertificateUtils.GetDomainFromCommonName(cert.Subject);
        }
    }
}