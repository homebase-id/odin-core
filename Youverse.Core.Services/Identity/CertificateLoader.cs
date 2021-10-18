using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Youverse.Core.Services.Identity
{
    public static class CertificateLoader
    {
        /// <summary>
        /// Returns a <see cref="X509Certificate2"/> from public key certificate file.
        /// </summary>
        /// <param name="publicKeyPath">Path to the public key</param>
        /// <returns></returns>
        public static string LoadPublicKeyCertificateFromPath(string publicKeyPath)
        {
            using (X509Certificate2 publicKey = new X509Certificate2(publicKeyPath))
            {
                return publicKey.GetNameInfo(X509NameType.SimpleName, false);
            }
        }
        
        public static X509Certificate2 LoadPublicKeyCertificateXml(string xml)
        {
            throw new NotImplementedException("need a keeeeyyy");
            // using (X509Certificate2 cert = new X509Certificate2())
            // {
            //     cert.PublicKey.Key.FromXmlString(xml);
            //     //return publicKey;
            // }
        }

        /// <summary>
        /// Returns a <see cref="X509Certificate2"/> from an RSA public and private key files.
        /// </summary>
        /// <param name="publicKeyPath">Path to the public key .cer or .crt file</param>
        /// <param name="privateKeyPath">Path to the private key file</param>
        /// <returns></returns>
        public static X509Certificate2 LoadPublicPrivateRSAKey(string publicKeyPath, string privateKeyPath)
        {
            using (X509Certificate2 publicKey = new X509Certificate2(publicKeyPath))
            {
                string encodedKey = File.ReadAllText(privateKeyPath);
                RSA rsaPrivateKey;
                using (rsaPrivateKey = RSA.Create())
                {
                    rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());

                    using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                    {
                        //_logger.LogInformation($"Certificate resolved for [{hostname}]");

                        // Export as PFX and re-import if you want "normal PFX private key lifetime"
                        // (this step is currently required for SslStream, but not for most other things
                        // using certificates)
                        return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    }
                }
            }
        }


        /// <summary>
        /// Convert a hexidecimal string to a byte[]
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        /// <remarks>/// Via https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa/26304129#26304129</remarks>
        public static byte[] GetByteArray(string hexadecimalString)
        {
            var outputLength = hexadecimalString.Length / 2;
            var output = new byte[outputLength];
            var numeral = new char[2];
            for (int i = 0; i < outputLength; i++)
            {
                hexadecimalString.CopyTo(i * 2, numeral, 0, 2);
                output[i] = Convert.ToByte(new string(numeral), 16);
            }
            return output;
        }
    }
}
