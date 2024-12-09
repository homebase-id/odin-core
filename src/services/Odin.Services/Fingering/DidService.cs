using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.Fingering;

#nullable enable

public interface IDidService
{
    public Task<DidWebResponse?> GetDidWebAsync();
}

public class DidService(
    OdinContext context,
    StaticFileContentService staticFileContentService)
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

        var result = new DidWebResponse
        {
            Id = $"did:web:{domain}",
            Controller = $"did:web:{domain}",
            VerificationMethod = [] // SEB:TODO
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
    public List<object>? VerificationMethod { get; set; }

    [JsonPropertyName("service")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DidWebService> Service { get; set; } = [];
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