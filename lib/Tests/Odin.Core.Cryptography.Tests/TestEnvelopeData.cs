using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using Odin.Core;
using Odin.Core.Identity;
using System.Text;

namespace Odin.Tests
{
    public class EnvelopeDataTests
    {
        /// <summary>
        /// Example of how to use Envelope and Signature and SignedEnvelope.
        /// This is the biggest example including two signatories (each independent)
        /// rounded off by a Notary Public that has verified each signature and signed
        /// the whole document.
        /// </summary>
        [Test]
        public void Envelope_Signature_Example()
        {
            // Let's say we have a document (possibly a file)
            byte[] document = new byte[] { 1, 2, 3, 4, 5 };
            // We want some additional information in the envelope
            var additionalInfo = new SortedDictionary<string, object> { { "title", "test document" }, { "serialno", 42 } };

            // Create an Envelope for this document
            var envelope = new EnvelopeData();
            envelope.CalculateContentHash(document, "test", additionalInfo);

            // Create an identity and keys needed
            OdinId testIdentity = new OdinId("odin.valhalla.com");
            SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, 1);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(testIdentity, testKeyPwd, testEccKey);

            // Create a second identity and keys needed
            OdinId testIdentity2 = new OdinId("thor.valhalla.com");
            SensitiveByteArray testKeyPwd2 = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey2 = new EccFullKeyData(testKeyPwd2, 1);

            //  Now let's sign the envelope with the second signature.
            signedEnvelope.CreateEnvelopeSignature(testIdentity2, testKeyPwd2, testEccKey2);

            // Create a notary public identity and keys needed
            OdinId testIdentity3 = new OdinId("notarius.publicus.com");
            SensitiveByteArray testKeyPwd3 = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey3 = new EccFullKeyData(testKeyPwd3, 1);

            // Yet another identity, but we skip it for now. POC.
            signedEnvelope.SignNotariusPublicus(testIdentity3, testKeyPwd3, testEccKey3);

            signedEnvelope.VerifyEnvelopeSignatures();

            // string s = signedEnvelope.GetCompactSortedJson();
        }

        public static string StringifyData(SortedDictionary<string, object> data)
        {
            StringBuilder sb = new StringBuilder();

            foreach (KeyValuePair<string, object> entry in data)
            {
                sb.Append(entry.Key);
                sb.Append(":");

                if (entry.Value is SortedDictionary<string, string> nestedDict)
                {
                    foreach (KeyValuePair<string, string> nestedEntry in nestedDict)
                    {
                        sb.Append(nestedEntry.Key);
                        sb.Append("=");
                        sb.Append(nestedEntry.Value);
                        sb.Append(";");
                    }
                }
                else
                {
                    sb.Append(entry.Value.ToString());
                }

                sb.Append(",");
            }

            return sb.ToString();
        }

        [Test]
        public void VerifiedIdentityExperiment()
        {
            // Let's say we have a document (possibly a file)
            // We want some additional information in the envelope
            var additionalInfo = new SortedDictionary<string, object>
            {
                { "identity", "frodo.baggins.me" },
                { "issued", "2023-06-10" },
                { "expiration", "2028-06-10" },
                { "authority", "id.verifyssi.com" },
                { "URL", "https://api.verifyssi.com/api/v1/verify?prpt=$signature" },  // Replace $signature with the signatureBase64
                { "attestationFormat", "personalInfo" },  // Can be "personalInfo", "humanVerification", ... more to come ...
                { "data", new SortedDictionary<string, object>
                    {
                        { "FN", "Frodo Baggins" },
                        { "ADR", new SortedDictionary<string, string>
                            {
                                { "street", "Bag End" },
                                { "city", "Hobbiton" },
                                { "region", "The Shire" },
                                { "postalCode", "4242" },
                                { "country", "Middleearth" }
                            }
                        }
                    }
                }
            };

            SortedDictionary<string, object> data = (SortedDictionary<string, object>)additionalInfo["data"];
            string doc = StringifyData(data);
            byte[] content = doc.ToUtf8ByteArray();

            // Create an Envelope for this document
            var envelope = new EnvelopeData();
            envelope.CalculateContentHash(content, "attestation", additionalInfo);

            // Create an identity and keys needed
            OdinId testIdentity = new OdinId("id.verifyssi.com");
            SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, 1);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(testIdentity, testKeyPwd, testEccKey);


            signedEnvelope.VerifyEnvelopeSignatures();

            string s = signedEnvelope.GetCompactSortedJson();
        }


        [Test]
        public void CalculateDocumentHash_WithByteArray_CorrectlyInitializesProperties()
        {
            // Arrange
            byte[] document = new byte[] { 1, 2, 3, 4, 5 };
            var additionalInfo = new SortedDictionary<string, object> { { "title", "test document" }, { "serialno", 7 } };
            var envelope = new EnvelopeData();

            // Act
            envelope.CalculateContentHash(document, "test", additionalInfo);

            // Assert
            Assert.IsNotNull(envelope.ContentHash);
            Assert.IsNotNull(envelope.Nonce);
            Assert.AreEqual(HashUtil.SHA256Algorithm, envelope.ContentHashAlgorithm);
            Assert.IsNotNull(envelope.TimeStamp);
            Assert.AreEqual(document.Length, envelope.Length);
            Assert.AreEqual(additionalInfo, envelope.AdditionalInfo);
        }

        [Test]
        public void CalculateDocumentHash_WithByteArray_CorrectlyInitializesProperties2()
        {
            // Arrange
            byte[] document = new byte[] { 1, 2, 3, 4, 5 };
            var envelope = new EnvelopeData();

            // Act
            envelope.CalculateContentHash(document, "test", null);

            // Assert
            Assert.IsNotNull(envelope.ContentHash);
            Assert.IsNotNull(envelope.Nonce);
            Assert.AreEqual(HashUtil.SHA256Algorithm, envelope.ContentHashAlgorithm);
            Assert.IsNotNull(envelope.TimeStamp);
            Assert.AreEqual(document.Length, envelope.Length);
            Assert.AreEqual(null, envelope.AdditionalInfo);
        }

        [Test]
        public void CalculateDocumentHash_WithFileName_CorrectlyInitializesProperties()
        {
            // Arrange
            string fileName = Path.GetTempFileName();
            File.WriteAllBytes(fileName, new byte[] { 1, 2, 3, 4, 5 });
            var additionalInfo = new SortedDictionary<string, object> { { "title", "test document" }, { "author", "Odin" } };
            var envelope = new EnvelopeData();

            // Act
            envelope.CalculateContetntHash(fileName, "test", additionalInfo);

            // Assert
            Assert.IsNotNull(envelope.ContentHash);
            Assert.IsNotNull(envelope.Nonce);
            Assert.AreEqual(HashUtil.SHA256Algorithm, envelope.ContentHashAlgorithm);
            Assert.IsNotNull(envelope.TimeStamp);
            Assert.AreEqual(new FileInfo(fileName).Length, envelope.Length);
            Assert.AreEqual(additionalInfo, envelope.AdditionalInfo);

            // Clean up
            File.Delete(fileName);
        }
    }
}
