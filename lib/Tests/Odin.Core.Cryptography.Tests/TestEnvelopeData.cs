﻿using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using Odin.Core;
using Odin.Core.Identity;
using System.Text;
using Odin.Core.Util;
using NodaTime;
using Odin.Core.Time;
using System.Globalization;

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

        public static void Attestation(PunyDomainName identity, SortedDictionary<string, object> dataToAttest)
        {
            const string AUTHORITY_IDENTITY = "id.verifyssi.com";
            const string VERIFYURL = "https://api.verifyssi.com/api/v1/verify?prpt=$signature"; // Replace $signature with the signatureBase64 when calling
            const string ATTESTATIONTYPE_PERSONALINFO = "personalInfo";
            string USAGEPOLICY_URL = $"https://{identity.DomainName}/policies/attestation-usage-policy";

            const string CONTENTTYPE_ATTESTATION = "attestation";

            // Verify dataToAttest is not null and contains data
            if (dataToAttest == null || dataToAttest.Count == 0)
            {
                throw new ArgumentException("Invalid attestation data. Please ensure that dataToAttest contains data.");
            }

            // Let's say we have a document (possibly a file)
            // We want some additional information in the envelope
            var additionalInfo = new SortedDictionary<string, object>
            {
                { "identity", identity.DomainName },
                { "issued", ((Instant) UnixTimeUtc.Now()).InUtc().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "expiration", ((Instant) UnixTimeUtc.Now().AddSeconds(3600*24*365*5)).InUtc().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "authority", AUTHORITY_IDENTITY },
                { "URL", VERIFYURL },  
                { "attestationFormat", ATTESTATIONTYPE_PERSONALINFO },
                { "usagePolicyUrl", USAGEPOLICY_URL },
                { "data", dataToAttest }  // Insert dataToAttest here
            };

            // Make sure it's valid
            EnvelopeData.VerifyAdditionalInfoTypes(additionalInfo);

            SortedDictionary<string, object> data = (SortedDictionary<string, object>)additionalInfo["data"];
            string doc = StringifyData(data);
            byte[] content = doc.ToUtf8ByteArray();

            // Create an Envelope for this document
            var envelope = new EnvelopeData();
            envelope.CalculateContentHash(content, CONTENTTYPE_ATTESTATION, additionalInfo);

            // Create an identity and keys needed
            OdinId testIdentity = new OdinId(AUTHORITY_IDENTITY);
            SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, 1);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(testIdentity, testKeyPwd, testEccKey);

            // Check everything is dandy
            signedEnvelope.VerifyEnvelopeSignatures();

            // For michael to look at the JSON
            string s = signedEnvelope.GetCompactSortedJson();
        }


        // This function attests that the OdinId is associated with a human.
        void AttestHuman(PunyDomainName identity)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { "IsHuman", true }
            };

            Attestation(identity, dataToAttest);
        }

        // This function attests to the legal name of the owner of the OdinId.
        void AttestLegalName(PunyDomainName identity, string legalName)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { "LegalName", legalName }
            };

            Attestation(identity, dataToAttest);
        }

        // This function attests to the residential address of the owner of the OdinId.
        void AttestResidentialAddress(PunyDomainName identity, SortedDictionary<string, string> address)
        {
            var dataToAttest = new SortedDictionary<string, object>
    {
        { "ResidentialAddress", address }
    };

            Attestation(identity, dataToAttest);
        }

        // This function attests to the email address of the owner of the OdinId.
        void AttestEmailAddress(PunyDomainName identity, string emailAddress)
        {
            var dataToAttest = new SortedDictionary<string, object>
    {
        { "EmailAddress", emailAddress }
    };

            Attestation(identity, dataToAttest);
        }

        // This function attests to the phone number of the owner of the OdinId.
        void AttestPhoneNumber(PunyDomainName identity, string phoneNumber)
        {
            var dataToAttest = new SortedDictionary<string, object>
    {
        { "PhoneNumber", phoneNumber }
    };

            Attestation(identity, dataToAttest);
        }

        // This function attests to the birthdate of the owner of the OdinId.
        void AttestBirthdate(PunyDomainName identity, DateTime birthdate)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { "Birthdate", birthdate }
            };

            Attestation(identity, dataToAttest);
        }

        // This function attests to the nationality of the owner of the OdinId.
        void AttestNationality(PunyDomainName identity, string nationality)
        {
            var dataToAttest = new SortedDictionary<string, object>
            {
                { "Nationality", nationality }
            };

            Attestation(identity, dataToAttest);
        }


        [Test]
        public void VerifiedIdentityExperiment()
        {
            var dataToAttest = new SortedDictionary<string, object>
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
            };


            Attestation(new PunyDomainName("frodo.baggins.me"), dataToAttest);
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
