using JsonLd.Normalization;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Time;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Odin.Core.Cryptography.Crypto;

// When it is live, eventually test with
//
// didkit vc-verify-credential -v EcdsaSecp384r1Signature2019 credential.json
//
// Eventually test with
// didkit vc-verify-credential -v EcdsaSecp384r1Signature2019 credential.json

public class VCCredentialsResponse
{
    [JsonPropertyName("@context")]
    public List<object> Context { get; set; } = new List<object> { "https://www.w3.org/2018/credentials/v1" };

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("type")]
    public List<string> Type { get; set; }

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    [JsonPropertyName("issuanceDate")]
    public string IssuanceDate { get; set; }

    [JsonPropertyName("credentialSubject")]
    public VCCredentialSubject Subject { get; set; }

    [JsonPropertyName("proof")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VCProof Proof { get; set; }
}

public class VCCredentialSubject
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
}

public class VCProof
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("created")]
    public string Created { get; set; }

    [JsonPropertyName("proofPurpose")]
    public string ProofPurpose { get; set; }

    [JsonPropertyName("verificationMethod")]
    public string VerificationMethod { get; set; }

    [JsonPropertyName("jws")]
    public string Jws { get; set; }
}


public static class VerifiableCredentialsManager
{
    /// <summary>
    /// Creates a Verifiable Credential (VC) JSON without the proof.
    /// </summary>
    /// <param name="odinId">The OdinId containing the domain name for the DID.</param>
    /// <param name="attributes">Dictionary of key-value pairs for credentialSubject (e.g., fullName, firstName).</param>
    /// <param name="credentialType">The specific type of the VC (e.g., IdentityCredential).</param>
    /// <returns>The VC as a JsonObject without proof.</returns>
    public static VCCredentialsResponse CreateVerifiableCredential(
            OdinId odinId,
            List<(string Key, string Value, string ContextUri)> attributes,
            string credentialType = "IdentityCredential")
    {
        var did = "did:web:" + odinId.DomainName;
        var contextObject = new Dictionary<string, string> { [credentialType] = "https://schema.org/Person" };
        var subject = new VCCredentialSubject { Id = did };
        foreach (var (key, value, contextUri) in attributes)
        {
            subject.Attributes[key] = value;
            contextObject[key] = contextUri;
        }
        var credential = new VCCredentialsResponse
        {
            Context = new List<object> { "https://www.w3.org/2018/credentials/v1", contextObject },
            Id = $"urn:uuid:{Guid.NewGuid()}",
            Type = new List<string> { "VerifiableCredential", credentialType },
            Issuer = did,
            IssuanceDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Subject = subject
        };
        return credential;
    }


    /// <summary>
    /// Creates the byte array to sign for a Verifiable Credential by canonicalizing
    /// the credential and proof JSON using URDNA2015 (JSON-LD normalization) and
    /// concatenating their SHA-256 hashes, as per W3C Verifiable Credentials Data Model v1.1.
    /// </summary>
    /// <param name="credential">The Verifiable Credential JsonObject without proof.</param>
    /// <param name="proof">The proof JsonObject without the jws field.</param>
    /// <returns>The concatenated SHA-256 hashes of the canonicalized credential and proof.</returns>
    /// <remarks>
    /// Uses json-ld.net for URDNA2015 canonicalization to ensure W3C-compliant signatures.
    /// The resulting byte array is signed to produce the jws for the proof.
    /// </remarks>

    public async static Task<byte[]> CreateDataToSignAsync(VCCredentialsResponse credential, VCProof proof)
    {
        string credentialJson = System.Text.Json.JsonSerializer.Serialize(credential);
        string docNQuads = await JsonLdHandler.NormalizeAsync(credentialJson);
        byte[] docHash = DigestUtilities.CalculateDigest("SHA-256", Encoding.UTF8.GetBytes(docNQuads));
        string proofJson = System.Text.Json.JsonSerializer.Serialize(proof);
        string proofNQuads = await JsonLdHandler.NormalizeAsync(proofJson);
        byte[] proofHash = DigestUtilities.CalculateDigest("SHA-256", Encoding.UTF8.GetBytes(proofNQuads));
        byte[] toSign = new byte[proofHash.Length + docHash.Length];
        Array.Copy(proofHash, toSign, proofHash.Length);
        Array.Copy(docHash, 0, toSign, proofHash.Length, docHash.Length);

        return toSign;
    }

    /// <summary>
    /// Signs a Verifiable Credential (VC) using ECC-384 with EccFullKeyData and adds the proof to it.
    /// </summary>
    /// <param name="credential">The VC JsonObject without proof.</param>
    /// <param name="eccFullKey">The EccFullKeyData containing the ECC-384 private key.</param>
    /// <param name="secret">The SensitiveByteArray used to decrypt the private key.</param>
    /// <param name="verificationMethodId">The verification method ID (e.g., DID URL for the signing key).</param>
    /// <returns>The signed VC as a JSON string with proof added.</returns>
    public async static Task<string> SignVerifiableCredentialAsync(
            VCCredentialsResponse credential,
            EccFullKeyData eccFullKey,
            SensitiveByteArray secret,
            string verificationMethodId)
    {
        var proof = new VCProof
        {
            Type = "EcdsaSecp384r1Signature2019",
            Created = UnixTimeUtc.Now().Iso9441(),
            ProofPurpose = "assertionMethod",
            VerificationMethod = verificationMethodId
        };
        byte[] toSign = await CreateDataToSignAsync(credential, proof);
        byte[] signature = eccFullKey.Sign(secret, toSign);
        proof.Jws = WebEncoders.Base64UrlEncode(signature);
        credential.Proof = proof;
        return System.Text.Json.JsonSerializer.Serialize(credential);
    }


    /// <summary>
    /// Extracts the original signature bytes from the jws in the credential's proof.
    /// </summary>
    /// <param name="proof">The JsonObject containing the proof with the jws field.</param>
    /// <returns>The original signature bytes produced by EccFullKeyData.Sign.</returns>
    /// <exception cref="ArgumentNullException">Thrown if proof or jws is null.</exception>
    /// <exception cref="FormatException">Thrown if jws is not a valid base64url string.</exception>
    public static byte[] ExtractSignatureFromProof(VCProof proof)
    {
        if (proof == null)
            throw new ArgumentNullException(nameof(proof));
        string jws = proof.Jws;
        if (string.IsNullOrEmpty(jws))
            throw new ArgumentNullException(nameof(jws), "jws field is missing or empty in proof");

        return WebEncoders.Base64UrlDecode(jws);
    }


    public async static Task<bool> VerifySignatureAsync(string vcJson, EccPublicKeyData eccPublicKey)
    {
        var credential = System.Text.Json.JsonSerializer.Deserialize<VCCredentialsResponse>(vcJson);
        var proof = credential.Proof;
        var signature = ExtractSignatureFromProof(proof);

        credential.Proof = null;
        var proofNoJws = new VCProof
        {
            Type = proof.Type,
            Created = proof.Created,
            ProofPurpose = proof.ProofPurpose,
            VerificationMethod = proof.VerificationMethod
        };

        var toVerify = await CreateDataToSignAsync(credential, proofNoJws);
        return eccPublicKey.VerifySignature(toVerify, signature);
    }
}
