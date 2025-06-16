using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Odin.Core.X509;

public static class X509Extensions
{
    public static byte[] CreateSignature(this X509Certificate2 certificate, byte[] buffer)
    {
        using var key = certificate.GetECDsaPrivateKey();
        if (key == null)
        {
            throw new ArgumentException("Certificate does not have an ECDSA private key.");
        }
        return key.SignData(buffer, HashAlgorithmName.SHA256);
    }

    //

    public static byte[] CreateSignature(this X509Certificate2 certificate, string text)
    {
        return CreateSignature(certificate, text.ToUtf8ByteArray());
    }

    //

    public static bool VerifySignature(this X509Certificate2 certificate, byte[] signature, byte[] originalData)
    {
        using var key = certificate.GetECDsaPublicKey();
        if (key == null)
        {
            throw new ArgumentException("Certificate does not have an ECDSA public key.");
        }
        return key.VerifyData(originalData, signature, HashAlgorithmName.SHA256);
    }

    //

    public static bool VerifySignature(this X509Certificate2 certificate, byte[] signature, string originalText)
    {
        return VerifySignature(certificate, signature, originalText.ToUtf8ByteArray());
    }

    //

    public static X509Certificate2 CreateSelfSignedEcDsaCertificate(string domain)
    {
        var subject = new X500DistinguishedName($"CN={domain}");

        // Create ECDsa key for the certificate
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256); // Or use another curve like nistP384 or nistP521
        var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);

        // Set certificate validity period
        var notBefore = DateTimeOffset.UtcNow.AddYears(-10);
        var notAfter = DateTimeOffset.UtcNow.AddYears(10);

        // Create self-signed certificate
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    //

    public static List<string> GetSubjectAlternativeNames(this X509Certificate2 cert)
    {
        var result = new List<string>();

        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.5.29.17") // Subject Alternative Name
            {
                // Decode asn encoded SAN extension
                var asnReader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
                var seq = asnReader.ReadSequence();
                while (seq.HasData)
                {
                    var rawTag = seq.PeekTag();
                    // DNS Name tag is [2] (context-specific, primitive)
                    if (rawTag.TagClass == TagClass.ContextSpecific && rawTag.TagValue == 2)
                        result.Add(seq.ReadCharacterString(UniversalTagNumber.IA5String, rawTag));
                    else
                        seq.ReadEncodedValue();
                }
            }
        }

        return result;
    }

}