using JsonLd.Normalization;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Odin.Core.Cryptography.Crypto;

public class CredentialsResponse
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
    public CredentialSubject Subject { get; set; }

    [JsonPropertyName("proof")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Proof Proof { get; set; }
}

public class CredentialSubject
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
}

public class Proof
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
    public static JsonObject CreateVerifiableCredential(
        OdinId odinId,
        List<(string Key, string Value, string ContextUri)> attributes,
        string credentialType = "IdentityCredential")
    {
        var did = "did:web:" + odinId.DomainName;
        var credentialSubject = new JsonObject { ["id"] = did };
        var contextObject = new JsonObject
        {
            [credentialType] = "https://schema.org/Person"
        };
        foreach (var (key, value, contextUri) in attributes)
        {
            credentialSubject[key] = value;
            contextObject[key] = contextUri;
        }
        var credential = new JsonObject
        {
            ["@context"] = new JsonArray(
                "https://www.w3.org/2018/credentials/v1",
                contextObject
            ),
            ["id"] = $"urn:uuid:{Guid.NewGuid()}",
            ["type"] = new JsonArray("VerifiableCredential", credentialType),
            ["issuer"] = did,
            ["issuanceDate"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["credentialSubject"] = credentialSubject
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

    public async static Task<byte[]> CreateDataToSign(JsonObject credential, JsonObject proof)
    {
        string credentialJson = credential.ToJsonString();
        string docNQuads = await JsonLdHandler.Normalize(credentialJson);
        byte[] docHash = DigestUtilities.CalculateDigest("SHA-256", Encoding.UTF8.GetBytes(docNQuads));
        string proofJson = proof.ToJsonString();
        string proofNQuads = await JsonLdHandler.Normalize(proofJson);
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
    /// <param name="keyData">The EccFullKeyData containing the ECC-384 private key.</param>
    /// <param name="encryptionKey">The SensitiveByteArray used to decrypt the private key.</param>
    /// <param name="verificationMethodId">The verification method ID (e.g., DID URL for the signing key).</param>
    /// <returns>The signed VC as a JSON string with proof added.</returns>
    public async static Task<string> SignVerifiableCredential(
        JsonObject credential,
        EccFullKeyData keyData,
        SensitiveByteArray encryptionKey,
        string verificationMethodId)
    {
        var proof = new JsonObject
        {
            ["type"] = "EcdsaSecp384r1Signature2019",
            ["created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["proofPurpose"] = "assertionMethod",
            ["verificationMethod"] = verificationMethodId
        };

        // Create data to sign
        byte[] toSign = await CreateDataToSign(credential, proof);

        // Sign with EccFullKeyData
        byte[] signature = keyData.Sign(encryptionKey, toSign);

        // Create base64url JWS
        string jws = Convert.ToBase64String(signature)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        proof["jws"] = jws;

        // Add proof to credential
        credential["proof"] = proof;

        // Return signed VC JSON
        return credential.ToJsonString();
    }

    /// <summary>
    /// Extracts the original signature bytes from the jws in the credential's proof.
    /// </summary>
    /// <param name="proof">The JsonObject containing the proof with the jws field.</param>
    /// <returns>The original signature bytes produced by EccFullKeyData.Sign.</returns>
    /// <exception cref="ArgumentNullException">Thrown if proof or jws is null.</exception>
    /// <exception cref="FormatException">Thrown if jws is not a valid base64url string.</exception>
    public static byte[] ExtractSignatureFromProof(JsonObject proof)
    {
        if (proof == null)
            throw new ArgumentNullException(nameof(proof));

        string jws = proof["jws"]?.ToString();
        if (string.IsNullOrEmpty(jws))
            throw new ArgumentNullException(nameof(jws), "jws field is missing or empty in proof");

        // Convert base64url to base64 (add padding if needed)
        string base64 = jws.Replace('-', '+').Replace('_', '/');
        int mod4 = base64.Length % 4;
        if (mod4 > 0)
        {
            base64 += new string('=', 4 - mod4);
        }

        // Decode base64 to original signature bytes
        return Convert.FromBase64String(base64);
    }
}