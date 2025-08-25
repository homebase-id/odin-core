using JsonLd.Normalization;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Odin.Core.Cryptography.Tests
{
    [TestFixture]
    public class VerifiableCredentialsManagerTests
    {
        private OdinId _odinId;
        private EccFullKeyData _keyData;
        private SensitiveByteArray _encryptionKey;

        [SetUp]
        public void Setup()
        {
            // Setup test data
            _odinId = new OdinId("frodo.baggins.demo.rocks");
            _encryptionKey = new SensitiveByteArray(ByteArrayUtil.GetRandomCryptoGuid().ToByteArray());
            _keyData = new EccFullKeyData(_encryptionKey, EccKeySize.P384, hours: 24);
        }


        [Test]
        public async Task CreateAndSignIdentityVc_ShouldProduceURDNA2015CompliantVc()
        {
            // Arrange
            var attributes = new List<(string Key, string Value, string ContextUri)>
            {
                ("fullName", "Frodo Baggins", "https://schema.org/name"),
                ("email", "mail@frodo.baggins.demo.rocks", "https://schema.org/email")
            };
            var vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "IdentityCredential");
            string verificationMethod = "did:web:frodobaggins.me#signing-key";
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredentialAsync(vc, _keyData, _encryptionKey, verificationMethod);
            var parsedVc = System.Text.Json.JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson);
            ClassicAssert.That(System.Text.Json.JsonSerializer.Serialize(parsedVc.Context), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(new List<object>
                    {
                        "https://www.w3.org/2018/credentials/v1",
                        new Dictionary<string, string>
                        {
                            ["IdentityCredential"] = "https://schema.org/Person",
                            ["fullName"] = "https://schema.org/name",
                            ["email"] = "https://schema.org/email"
                        }
                    })));
            
            ClassicAssert.That(parsedVc.Id, Does.StartWith("urn:uuid:"), "ID should be a UUID");
            ClassicAssert.That(parsedVc.Type, Is.EquivalentTo(new List<string> { "VerifiableCredential", "IdentityCredential" }), "Type should match");
            ClassicAssert.That(parsedVc.Issuer, Is.EqualTo("did:web:frodo.baggins.demo.rocks"), "Issuer should match DID");
            ClassicAssert.That(parsedVc.IssuanceDate, Is.Not.Null, "Issuance date should be set");
            ClassicAssert.That(parsedVc.Subject.Id, Is.EqualTo("did:web:frodo.baggins.demo.rocks"), "Subject ID should match DID");
            ClassicAssert.That(parsedVc.Subject.Attributes["fullName"]?.ToString(), Is.EqualTo("Frodo Baggins"), "Full name should match");
            ClassicAssert.That(parsedVc.Subject.Attributes["email"]?.ToString(), Is.EqualTo("mail@frodo.baggins.demo.rocks"), "Email should match");
            ClassicAssert.That(parsedVc.Proof, Is.Not.Null, "Proof should exist");
            ClassicAssert.That(parsedVc.Proof.Type, Is.EqualTo("EcdsaSecp384r1Signature2019"), "Proof type should match");
            ClassicAssert.That(parsedVc.Proof.Jws, Is.Not.Null, "JWS should be set");

            // Verify URDNA2015 compliance
            var credentialNoProof = JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson);
            credentialNoProof.Proof = null;
            var proofNoJws = new VCProof
            {
                Type = parsedVc.Proof.Type,
                Created = parsedVc.Proof.Created,
                ProofPurpose = parsedVc.Proof.ProofPurpose,
                VerificationMethod = parsedVc.Proof.VerificationMethod
            };

            string credentialNQuads = await JsonLdHandler.NormalizeAsync(JsonSerializer.Serialize(credentialNoProof));
            string proofNQuads = await JsonLdHandler.NormalizeAsync(JsonSerializer.Serialize(proofNoJws));
            byte[] docHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(credentialNQuads));
            byte[] proofHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(proofNQuads));
            byte[] toVerify = new byte[proofHash.Length + docHash.Length];
            Array.Copy(proofHash, toVerify, proofHash.Length);
            Array.Copy(docHash, 0, toVerify, proofHash.Length, docHash.Length);
            var signature = VerifiableCredentialsManager.ExtractSignatureFromProof(parsedVc.Proof);
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };
            ClassicAssert.That(publicKeyData.VerifySignature(toVerify, signature), Is.True, "Signature should be URDNA2015 compliant");
        }

        [Test]
        public async Task CreateAndSignSshKeyVc_ShouldProduceURDNA2015CompliantVc()
        {
            // Arrange
            var attributes = new List<(string Key, string Value, string ContextUri)>
            {
                ("sshPublicKey", "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBL3... frodo.baggins@demo.rocks", "https://schema.org/identifier")
            };
            string verificationMethod = "did:web:frodo.baggins.demo.rocks#key-authentication";

            // Act
            VCCredentialsResponse vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "SSHKeyCredential");
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredentialAsync(vc, _keyData, _encryptionKey, verificationMethod);
            var parsedVc = JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson);

            // Assert VC structure
            ClassicAssert.That(parsedVc, Is.Not.Null, "VC should not be null");
            ClassicAssert.That(JsonSerializer.Serialize(parsedVc.Context), Is.EqualTo(JsonSerializer.Serialize(new List<object>
                {
                    "https://www.w3.org/2018/credentials/v1",
                    new Dictionary<string, string>
                    {
                        ["SSHKeyCredential"] = "https://schema.org/Person",
                        ["sshPublicKey"] = "https://schema.org/identifier"
                    }
                    })), "Context should match");
                        ClassicAssert.That(parsedVc.Id, Does.StartWith("urn:uuid:"), "ID should be a UUID");
            ClassicAssert.That(parsedVc.Type, Is.EquivalentTo(new List<string> { "VerifiableCredential", "SSHKeyCredential" }), "Type should match");
            ClassicAssert.That(parsedVc.Issuer, Is.EqualTo("did:web:frodo.baggins.demo.rocks"), "Issuer should match DID");
            ClassicAssert.That(parsedVc.IssuanceDate, Is.Not.Null, "Issuance date should be set");
            ClassicAssert.That(parsedVc.Subject.Id, Is.EqualTo("did:web:frodo.baggins.demo.rocks"), "Subject ID should match DID");
            ClassicAssert.That(parsedVc.Subject.Attributes["sshPublicKey"]?.ToString(), Does.StartWith("ecdsa-sha2-nistp384"), "SSH public key should match");
            ClassicAssert.That(parsedVc.Proof, Is.Not.Null, "Proof should exist");
            ClassicAssert.That(parsedVc.Proof.Type, Is.EqualTo("EcdsaSecp384r1Signature2019"), "Proof type should match");
            ClassicAssert.That(parsedVc.Proof.Jws, Is.Not.Null, "JWS should be set");

            // Verify URDNA2015 compliance
            var credentialNoProof = JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson);
            credentialNoProof.Proof = null;
            var proofNoJws = new VCProof
            {
                Type = parsedVc.Proof.Type,
                Created = parsedVc.Proof.Created,
                ProofPurpose = parsedVc.Proof.ProofPurpose,
                VerificationMethod = parsedVc.Proof.VerificationMethod
            };
            string credentialNQuads = await JsonLdHandler.NormalizeAsync(JsonSerializer.Serialize(credentialNoProof));
            string proofNQuads = await JsonLdHandler.NormalizeAsync(JsonSerializer.Serialize(proofNoJws));
            byte[] docHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(credentialNQuads));
            byte[] proofHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(proofNQuads));
            byte[] toVerify = new byte[proofHash.Length + docHash.Length];
            Array.Copy(proofHash, toVerify, proofHash.Length);
            Array.Copy(docHash, 0, toVerify, proofHash.Length, docHash.Length);
            var signature = VerifiableCredentialsManager.ExtractSignatureFromProof(parsedVc.Proof);
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };
            ClassicAssert.That(publicKeyData.VerifySignature(toVerify, signature), Is.True, "Signature should be URDNA2015 compliant");
        }

        [Test]
        public async Task CreateAndSignPublicKeyVc_ShouldProduceURDNA2015CompliantVc()
        {
            // Arrange
            var publicKeyJwk = _keyData.PublicKeyJwk();
            var attributes = new List<(string Key, string Value, string ContextUri)>
            {
                ("publicKeyJwk", publicKeyJwk, "https://w3id.org/security#publicKeyJwk")
            };
            string verificationMethod = "did:web:frodo.baggins.demo.rocks#key-authentication";

            // Act
            VCCredentialsResponse vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "PublicKeyCredential");
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredentialAsync(vc, _keyData, _encryptionKey, verificationMethod);
            var parsedVc = JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson);

            // Assert VC structure
            ClassicAssert.That(parsedVc, Is.Not.Null, "VC should not be null");
            ClassicAssert.That(JsonSerializer.Serialize(parsedVc.Context), Is.EqualTo(JsonSerializer.Serialize(new List<object>
            {
                "https://www.w3.org/2018/credentials/v1",
                new Dictionary<string, string>
                {
                    ["PublicKeyCredential"] = "https://schema.org/Person",
                    ["publicKeyJwk"] = "https://w3id.org/security#publicKeyJwk"
                }
            })), "Context should match");
            ClassicAssert.That(parsedVc.Id, Does.StartWith("urn:uuid:"), "ID should be a UUID");
            ClassicAssert.That(parsedVc.Type, Is.EquivalentTo(new List<string> { "VerifiableCredential", "PublicKeyCredential" }), "Type should match");
            ClassicAssert.That(parsedVc.Issuer, Is.EqualTo("did:web:frodo.baggins.demo.rocks"), "Issuer should match DID");
            ClassicAssert.That(parsedVc.IssuanceDate, Is.Not.Null, "Issuance date should be set");
            ClassicAssert.That(parsedVc.Subject.Id, Is.EqualTo("did:web:frodo.baggins.demo.rocks"), "Subject ID should match DID");
            ClassicAssert.That(parsedVc.Subject.Attributes["publicKeyJwk"]?.ToString(), Is.EqualTo(publicKeyJwk), "Public key JWK should match");
            ClassicAssert.That(parsedVc.Proof, Is.Not.Null, "Proof should exist");
            ClassicAssert.That(parsedVc.Proof.Type, Is.EqualTo("EcdsaSecp384r1Signature2019"), "Proof type should match");
            ClassicAssert.That(parsedVc.Proof.Jws, Is.Not.Null, "JWS should be set");

            // Verify URDNA2015 compliance
            var credentialNoProof = JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson);
            credentialNoProof.Proof = null;
            var proofNoJws = new VCProof
            {
                Type = parsedVc.Proof.Type,
                Created = parsedVc.Proof.Created,
                ProofPurpose = parsedVc.Proof.ProofPurpose,
                VerificationMethod = parsedVc.Proof.VerificationMethod
            };
            string credentialNQuads = await JsonLdHandler.NormalizeAsync(JsonSerializer.Serialize(credentialNoProof));
            string proofNQuads = await JsonLdHandler.NormalizeAsync(JsonSerializer.Serialize(proofNoJws));
            byte[] docHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(credentialNQuads));
            byte[] proofHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(proofNQuads));
            byte[] toVerify = new byte[proofHash.Length + docHash.Length];
            Array.Copy(proofHash, toVerify, proofHash.Length);
            Array.Copy(docHash, 0, toVerify, proofHash.Length, docHash.Length);
            var signature = VerifiableCredentialsManager.ExtractSignatureFromProof(parsedVc.Proof);
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };
            ClassicAssert.That(publicKeyData.VerifySignature(toVerify, signature), Is.True, "Signature should be URDNA2015 compliant");
        }
    }
}
