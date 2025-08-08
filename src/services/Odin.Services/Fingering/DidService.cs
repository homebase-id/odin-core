using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Optimization.Cdn;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Odin.Services.Fingering;

#nullable enable

public interface IDidService
{
    public Task<DidWebResponse?> GetDidWebAsync();
}

// Example validator: https://didlint.ownyourdata.eu/?did=did%3Aweb%3Afrodobaggins.me
// Another example validator: https://dev.uniresolver.io/
    
public class DidService(
    OdinContext context,
    StaticFileContentService staticFileContentService,
    PublicPrivateKeyService publicKeyService)
    : IDidService
{
    public async Task<DidWebResponse?> GetDidWebAsync()
    {
        var domain = context.Tenant.DomainName;

        var (_, fileExists, fileStream) = await staticFileContentService.GetStaticFileStreamAsync(
            StaticFileConstants.PublicProfileCardFileName);

        var profile = new StaticPublicProfile();
        if (fileExists && fileStream != null)
        {
            try
            {
                using var reader = new StreamReader(fileStream);
                var content = await reader.ReadToEndAsync();
                profile = OdinSystemSerializer.DeserializeOrThrow<StaticPublicProfile>(content);
            }
            finally
            {
                await fileStream.DisposeAsync();
            }
        }

        var signingPublicKey = await publicKeyService.GetSigningPublicKeyAsync();
        var publicKeyJwk = signingPublicKey?.PublicKeyJwk();
        var publicKeyJwkInstance = string.IsNullOrEmpty(publicKeyJwk)
            ? null
            : OdinSystemSerializer.DeserializeOrThrow<DidWebVerificationMethod.TPublicKeyJwk>(publicKeyJwk);

        var result = new DidWebResponse
        {
            Id = $"did:web:{domain}",
            Controller = $"did:web:{domain}",
            VerificationMethod =
            [
                new DidWebVerificationMethod
                {
                    Id = $"did:web:{domain}#key-authentication",
                    Type = "JsonWebKey2020",
                    Controller = $"did:web:{domain}",
                    PublicKeyJwk = publicKeyJwkInstance
                }
            ],
            Authentication = [$"did:web:{domain}#key-authentication"]
        };

        if (profile.Name != null)
        {
            result.DisplayName = profile.Name;
        }

        if (profile.Image != null)
        {
            result.Service.Add(new DidWebService
            {
                Id = $"did:web:{domain}#avatar",
                Type = "Image",
                ServiceEndpoint = profile.Image,
            });
        }

        var serviceIndex = 0; // ensures tag uniqueness

        if (profile.Email != null)
        {
            foreach (var email in profile.Email)
            {
                result.Service.Add(new DidWebService
                {
                    Id = $"did:web:{domain}#email-{++serviceIndex}",
                    Type = "Email",
                    ServiceEndpoint = $"mailto:{email.Email}",
                });
            }
        }

        if (profile.Links != null)
        {
            foreach (var link in profile.Links)
            {
                result.Service.Add(new DidWebService
                {
                    Id = $"did:web:{domain}#{link.Type}-{++serviceIndex}",
                    Type = "Profile",
                    ServiceEndpoint = link.Url,
                });
            }
        }

        /*
         * See comment on class. This is likely  to just be a static string output
        // Add Verifiable Credential if profile has attributes
        if (profile.Name != null || profile.Email != null)
        {
            var attributes = new List<(string Key, string Value, string ContextUri)>
            {
                ("fullName", profile.Name ?? "Unknown", "https://schema.org/name"),
                ("email", profile.Email?.FirstOrDefault()?.Email ?? "", "https://schema.org/email")
            };

            var vc = VerifiableCredentialsManager.CreateVerifiableCredential(
                new OdinId(domain),
                attributes,
                "IdentityCredential");

            string verificationMethod = $"did:web:{domain}#key-authentication";
            string signedVcJson = VerifiableCredentialsManager.SignVerifiableCredentialAsync(
                vc, publicKeyService.GetFullKeyData(), encryptionKey, verificationMethod);

            result.Credential.Add(JsonSerializer.Deserialize<VCCredentialsResponse>(signedVcJson));
        }
        */

        return result;
    }
}

public class DidWebResponse
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("controller")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Controller { get; set; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonPropertyName("verificationMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DidWebVerificationMethod>? VerificationMethod { get; set; }

    [JsonPropertyName("authentication")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Authentication { get; set; }

    [JsonPropertyName("service")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DidWebService> Service { get; set; } = [];

/*  This is likely just going to be stored as a (static) string,
 *  which would simply be the JSON string of the VCCredentialsResponse
 *  In other words this property is likely to just be a string
    // Verifiable Credentials (TBD)
    [JsonPropertyName("credential")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<VCCredentialsResponse>? Credential { get; set; }
*/
}

public class DidWebVerificationMethod
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("controller")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Controller { get; set; }

    [JsonPropertyName("publicKeyJwk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TPublicKeyJwk? PublicKeyJwk { get; set; }

    public class TPublicKeyJwk
    {
        [JsonPropertyName("kty")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kty { get; set; }

        [JsonPropertyName("crv")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Crv { get; set; }

        [JsonPropertyName("x")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? X { get; set; }

        [JsonPropertyName("y")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Y { get; set; }
    }
}

public class DidWebService
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("serviceEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServiceEndpoint { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}