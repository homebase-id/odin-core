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
using System.Text.Json.Nodes;
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
            _odinId = new OdinId("frodobaggins.me");
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
                ("email", "frodo.baggins@frodobaggins.me", "https://schema.org/email")
            };
            // Act
            JsonObject vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "IdentityCredential");

            string verificationMethod = "did:web:frodobaggins.me#signing-key";
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredential(vc, _keyData, _encryptionKey, verificationMethod);
            var parsedVc = JsonNode.Parse(signedVcJson).AsObject();
            Assert.That(parsedVc["@context"]?.ToJsonString(), Is.EqualTo(new JsonArray(
                "https://www.w3.org/2018/credentials/v1",
                new JsonObject
                {
                    ["IdentityCredential"] = "https://schema.org/Person",
                    ["fullName"] = "https://schema.org/name",
                    ["email"] = "https://schema.org/email"
                }
            ).ToJsonString()));

            Assert.That(parsedVc["id"]?.ToString(), Does.StartWith("urn:uuid:"));
            Assert.That(parsedVc["type"]?.AsArray().ToJsonString(), Is.EqualTo(new JsonArray("VerifiableCredential", "IdentityCredential").ToJsonString()));
            Assert.That(parsedVc["issuer"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            Assert.That(parsedVc["issuanceDate"], Is.Not.Null);
            var subject = parsedVc["credentialSubject"].AsObject();
            Assert.That(subject["id"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            Assert.That(subject["fullName"]?.ToString(), Is.EqualTo("Frodo Baggins"));
            Assert.That(subject["email"]?.ToString(), Is.EqualTo("frodo.baggins@frodobaggins.me"));
            var proof = parsedVc["proof"].AsObject();
            Assert.That(proof["type"]?.ToString(), Is.EqualTo("EcdsaSecp384r1Signature2019"));
            Assert.That(proof["jws"], Is.Not.Null);

            // Verify URDNA2015 compliance
            var credentialNoProof = parsedVc.DeepClone().AsObject();
            credentialNoProof.Remove("proof");
            var proofNoJws = new JsonObject
            {
                ["type"] = proof["type"]?.ToString(),
                ["created"] = proof["created"]?.ToString(),
                ["proofPurpose"] = proof["proofPurpose"]?.ToString(),
                ["verificationMethod"] = proof["verificationMethod"]?.ToString()
            };
            string credentialNQuads = await JsonLdHandler.Normalize(credentialNoProof.ToJsonString());
            string proofNQuads = await JsonLdHandler.Normalize(proofNoJws.ToJsonString());
            byte[] docHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(credentialNQuads));
            byte[] proofHash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(proofNQuads));
            byte[] toVerify = new byte[proofHash.Length + docHash.Length];
            Array.Copy(proofHash, toVerify, proofHash.Length);
            Array.Copy(docHash, 0, toVerify, proofHash.Length, docHash.Length);
            var signature = VerifiableCredentialsManager.ExtractSignatureFromProof(proof);
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };
            Assert.That(publicKeyData.VerifySignature(toVerify, signature), Is.True);
        }
    
        /*
        [Test]
        public async Task CreateAndSignIdentityVc_ShouldProduceValidVc()
        {
            // Arrange
            var attributes = new Dictionary<string, string>
            {
                { "fullName", "Frodo Baggins" },
                { "email", "frodo.baggins@frodobaggins.me" }
            };
            string verificationMethod = "did:web:frodobaggins.me#signing-key";

            // Act
            JsonObject vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "IdentityCredential");
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredential(vc, _keyData, _encryptionKey, verificationMethod);

            var mystring = signedVcJson.ToString();

            // Assert
            var parsedVc = JsonNode.Parse(signedVcJson).AsObject();
            ClassicAssert.That(parsedVc["@context"]?.ToJsonString(), Is.EqualTo(new JsonArray("https://www.w3.org/2018/credentials/v1").ToJsonString()));
            ClassicAssert.That(parsedVc["id"]?.ToString(), Does.StartWith("urn:uuid:"));
            ClassicAssert.That(parsedVc["type"]?.AsArray().ToJsonString(), Is.EqualTo(new JsonArray("VerifiableCredential", "IdentityCredential").ToJsonString()));
            ClassicAssert.That(parsedVc["issuer"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            ClassicAssert.That(parsedVc["issuanceDate"], Is.Not.Null);
            var subject = parsedVc["credentialSubject"].AsObject();
            ClassicAssert.That(subject["id"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            ClassicAssert.That(subject["fullName"]?.ToString(), Is.EqualTo("Frodo Baggins"));
            ClassicAssert.That(subject["email"]?.ToString(), Is.EqualTo("frodo.baggins@frodobaggins.me"));
            var proof = parsedVc["proof"].AsObject();
            ClassicAssert.That(proof["type"]?.ToString(), Is.EqualTo("EcdsaSecp384r1Signature2019"));
            ClassicAssert.That(proof["jws"], Is.Not.Null);

            // Verify signature
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };

            var originalByteArraySignature = VerifiableCredentialsManager.ExtractSignatureFromProof(proof);
            ClassicAssert.That(VerifySignature(signedVcJson, publicKeyData), Is.True);
        }

        [Test]
        public async Task CreateAndSignSshKeyVc_ShouldProduceValidVc()
        {
            // Arrange
            var attributes = new Dictionary<string, string>
            {
                { "sshPublicKey", "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBL3... frodo.baggins@frodobaggins.me" }
            };
            string verificationMethod = "did:web:frodobaggins.me#signing-key";

            // Act
            JsonObject vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "SSHKeyCredential");
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredential(vc, _keyData, _encryptionKey, verificationMethod);

            // Assert
            var parsedVc = JsonNode.Parse(signedVcJson).AsObject();
            ClassicAssert.That(parsedVc["@context"]?.ToJsonString(), Is.EqualTo(new JsonArray("https://www.w3.org/2018/credentials/v1").ToJsonString()));
            ClassicAssert.That(parsedVc["id"]?.ToString(), Does.StartWith("urn:uuid:"));
            ClassicAssert.That(parsedVc["type"]?.AsArray().ToJsonString(), Is.EqualTo(new JsonArray("VerifiableCredential", "SSHKeyCredential").ToJsonString()));
            ClassicAssert.That(parsedVc["issuer"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            ClassicAssert.That(parsedVc["issuanceDate"], Is.Not.Null);
            var subject = parsedVc["credentialSubject"].AsObject();
            ClassicAssert.That(subject["id"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            ClassicAssert.That(subject["sshPublicKey"]?.ToString(), Does.StartWith("ecdsa-sha2-nistp384"));
            var proof = parsedVc["proof"].AsObject();
            ClassicAssert.That(proof["type"]?.ToString(), Is.EqualTo("EcdsaSecp384r1Signature2019"));
            ClassicAssert.That(proof["jws"], Is.Not.Null);

            // Verify signature
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };
            ClassicAssert.That(VerifySignature(signedVcJson, publicKeyData), Is.True);
        }

        [Test]
        public async Task CreateAndSignMixedAttributesVc_ShouldProduceValidVc()
        {
            // Arrange
            var attributes = new Dictionary<string, string>
            {
                { "firstName", "Frodo" },
                { "lastName", "Baggins" },
                { "sshPublicKey", "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBL3... frodo.baggins@frodobaggins.me" }
            };
            string verificationMethod = "did:web:frodobaggins.me#signing-key";

            // Act
            JsonObject vc = VerifiableCredentialsManager.CreateVerifiableCredential(_odinId, attributes, "IdentityCredential");
            string signedVcJson = await VerifiableCredentialsManager.SignVerifiableCredential(vc, _keyData, _encryptionKey, verificationMethod);

            // Assert
            var parsedVc = JsonNode.Parse(signedVcJson).AsObject();
            ClassicAssert.That(parsedVc["@context"]?.ToJsonString(), Is.EqualTo(new JsonArray("https://www.w3.org/2018/credentials/v1").ToJsonString()));
            ClassicAssert.That(parsedVc["id"]?.ToString(), Does.StartWith("urn:uuid:"));
            ClassicAssert.That(parsedVc["type"]?.AsArray().ToJsonString(), Is.EqualTo(new JsonArray("VerifiableCredential", "IdentityCredential").ToJsonString()));
            ClassicAssert.That(parsedVc["issuer"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            ClassicAssert.That(parsedVc["issuanceDate"], Is.Not.Null);
            var subject = parsedVc["credentialSubject"].AsObject();
            ClassicAssert.That(subject["id"]?.ToString(), Is.EqualTo("did:web:frodobaggins.me"));
            ClassicAssert.That(subject["firstName"]?.ToString(), Is.EqualTo("Frodo"));
            ClassicAssert.That(subject["lastName"]?.ToString(), Is.EqualTo("Baggins"));
            ClassicAssert.That(subject["sshPublicKey"]?.ToString(), Does.StartWith("ecdsa-sha2-nistp384"));
            var proof = parsedVc["proof"].AsObject();
            ClassicAssert.That(proof["type"]?.ToString(), Is.EqualTo("EcdsaSecp384r1Signature2019"));
            ClassicAssert.That(proof["jws"], Is.Not.Null);

            // Verify signature
            var publicKeyData = new EccPublicKeyData { publicKey = _keyData.publicKey };
            ClassicAssert.That(VerifySignature(signedVcJson, publicKeyData), Is.True);
        }*/

        private async Task<bool> VerifySignature(string vcJson, EccPublicKeyData publicKeyData)
        {
            var vc = JsonNode.Parse(vcJson).AsObject();
            var proof = vc["proof"].DeepClone().AsObject();

            var signature = VerifiableCredentialsManager.ExtractSignatureFromProof(proof);
            
            var credentialNoProof = vc.DeepClone().AsObject();

            credentialNoProof.Remove("proof");
            proof.Remove("jws");

            var toVerify = await VerifiableCredentialsManager.CreateDataToSign(credentialNoProof, proof);

            return publicKeyData.VerifySignature(toVerify, signature);
        }
    }
}
