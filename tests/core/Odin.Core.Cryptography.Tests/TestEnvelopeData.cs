using NUnit.Framework;
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
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Cryptography.Data;

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
            var envelope = new EnvelopeData("test", "");
            envelope.SetAdditionalInfo(additionalInfo);
            envelope.CalculateContentHash(document);

            // Create an identity and keys needed
            OdinId testIdentity = new OdinId("odin.valhalla.com");
            SensitiveByteArray testKeyPwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey = new EccFullKeyData(testKeyPwd, EccKeySize.P384, 1);

            var signedEnvelope = new SignedEnvelope() { Envelope = envelope };

            //  Now let's sign the envelope.
            signedEnvelope.CreateEnvelopeSignature(testIdentity, testKeyPwd, testEccKey);

            // Create a second identity and keys needed
            OdinId testIdentity2 = new OdinId("thor.valhalla.com");
            SensitiveByteArray testKeyPwd2 = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey2 = new EccFullKeyData(testKeyPwd2, EccKeySize.P384, 1);

            //  Now let's sign the envelope with the second signature.
            signedEnvelope.CreateEnvelopeSignature(testIdentity2, testKeyPwd2, testEccKey2);

            // Create a notary public identity and keys needed
            OdinId testIdentity3 = new OdinId("notarius.publicus.com");
            SensitiveByteArray testKeyPwd3 = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            EccFullKeyData testEccKey3 = new EccFullKeyData(testKeyPwd3, EccKeySize.P384, 1);

            // Yet another identity, but we skip it for now. POC.
            signedEnvelope.SignNotariusPublicus(testIdentity3, testKeyPwd3, testEccKey3);

            signedEnvelope.VerifyEnvelopeSignatures();

            string s = signedEnvelope.GetCompactSortedJson();
        }


        [Test]
        public void VerifiedIdentityExperiment()
        {
            var pwd = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var eccKey = new EccFullKeyData(pwd, EccKeySize.P384, 1);
            var frodoPuny = new AsciiDomainName("frodo.baggins.me");
            var attestationId = Guid.NewGuid().ToByteArray();

            var attestation = AttestationManagement.AttestHuman(eccKey, pwd, frodoPuny, attestationId);
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Humaaaan");

            attestation = AttestationManagement.AttestNationality(eccKey, pwd, frodoPuny, attestationId, "DK");
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Nationality");

            attestation = AttestationManagement.AttestEmailAddress(eccKey, pwd, frodoPuny, attestationId, "frodo@baggins.me");
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Email");

            attestation = AttestationManagement.AttestBirthdate(eccKey, pwd, frodoPuny, attestationId, DateOnly.FromDateTime(new DateTime(2020,10,24)));
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Birthdate");

            attestation = AttestationManagement.AttestLegalName(eccKey, pwd, frodoPuny, attestationId, "Frodo Baggins");
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Legal Name");

            attestation = AttestationManagement.AttestSubsetLegalName(eccKey, pwd, frodoPuny, attestationId, "F. Baggins");
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Subset Legal Name");

            string s = attestation.GetCompactSortedJson(); // For michael to look at

            attestation = AttestationManagement.AttestPhoneNumber(eccKey, pwd, frodoPuny, attestationId, "+45 12345678");
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Phone number");

            var address = new SortedDictionary<string, object>
            { 
                { "street", "Bag End" },
                { "city", "Hobbiton" },
                { "region", "The Shire" },
                { "postalCode", "4242" },
                { "country", "Middle Earth" }
            };
            attestation = AttestationManagement.AttestResidentialAddress(eccKey, pwd, frodoPuny, attestationId, address);
            if (AttestationManagement.VerifyAttestation(attestation) != true)
                throw new Exception("Address");
        }


        [Test]
        public void CalculateDocumentHash_WithByteArray_CorrectlyInitializesProperties()
        {
            // Arrange
            byte[] document = new byte[] { 1, 2, 3, 4, 5 };
            var additionalInfo = new SortedDictionary<string, object> { { "title", "test document" }, { "serialno", 7 } };
            var envelope = new EnvelopeData("test", "");

            // Act
            envelope.SetAdditionalInfo(additionalInfo);
            envelope.CalculateContentHash(document);

            // Assert
            ClassicAssert.IsNotNull(envelope.ContentHash);
            ClassicAssert.IsNotNull(envelope.ContentNonce);
            ClassicAssert.AreEqual(HashUtil.SHA256Algorithm, envelope.ContentHashAlgorithm);
            ClassicAssert.IsNotNull(envelope.TimeStamp);
            ClassicAssert.AreEqual(document.Length, envelope.ContentLength);
            ClassicAssert.AreEqual(additionalInfo, envelope.AdditionalInfo);
        }

        [Test]
        public void CalculateDocumentHash_WithByteArray_CorrectlyInitializesProperties2()
        {
            // Arrange
            byte[] document = new byte[] { 1, 2, 3, 4, 5 };
            var envelope = new EnvelopeData("test", "");

            // Act
            envelope.CalculateContentHash(document);

            // Assert
            ClassicAssert.IsNotNull(envelope.ContentHash);
            ClassicAssert.IsNotNull(envelope.ContentNonce);
            ClassicAssert.AreEqual(HashUtil.SHA256Algorithm, envelope.ContentHashAlgorithm);
            ClassicAssert.IsNotNull(envelope.TimeStamp);
            ClassicAssert.AreEqual(document.Length, envelope.ContentLength);
            ClassicAssert.AreEqual(null, envelope.AdditionalInfo);
        }

        [Test]
        public void CalculateDocumentHash_WithFileName_CorrectlyInitializesProperties()
        {
            // Arrange
            string fileName = Path.GetTempFileName();
            File.WriteAllBytes(fileName, new byte[] { 1, 2, 3, 4, 5 });
            var additionalInfo = new SortedDictionary<string, object> { { "title", "test document" }, { "author", "Odin" } };
            var envelope = new EnvelopeData("test", "");

            // Act
            envelope.SetAdditionalInfo(additionalInfo);
            envelope.CalculateContentHash(fileName);

            // Assert
            ClassicAssert.IsNotNull(envelope.ContentHash);
            ClassicAssert.IsNotNull(envelope.ContentNonce);
            ClassicAssert.AreEqual(HashUtil.SHA256Algorithm, envelope.ContentHashAlgorithm);
            ClassicAssert.IsNotNull(envelope.TimeStamp);
            ClassicAssert.AreEqual(new FileInfo(fileName).Length, envelope.ContentLength);
            ClassicAssert.AreEqual(additionalInfo, envelope.AdditionalInfo);

            // Clean up
            File.Delete(fileName);
        }
    }
}
