using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.Fingering;

#nullable enable

public interface IWebfingerService
{
    public Task<WebFingerResponse?> GetWebFingerAsync();
}

public class WebfingerService(
    OdinContext context,
    StaticFileContentService staticFileContentService)
    : IWebfingerService
{
    public async Task<WebFingerResponse?> GetWebFingerAsync()
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

        var result = new WebFingerResponse
        {
            Subject = $"acct:@{domain}",
            Aliases =
            [
                $"https://{domain}/",
                $"acct:@{domain}"
            ],
            Properties = new Dictionary<string, string>
            {
                { "http://schema.org/url", $"https://{domain}/" },
            },
            Links =
            [
                new WebFingerLink
                {
                    Rel = "self",
                    Type = "application/json",
                    Href = $"https://{domain}/.well-known/webfinger",
                },

                new WebFingerLink
                {
                    Rel = "profile",
                    Type = "text/html",
                    Href = $"https://{domain}/",
                },
            ]
        };

        if (profile.Name != null)
        {
            result.Properties["http://schema.org/name"] = profile.Name;
        }

        if (profile.Image != null)
        {
            result.Links.Add(new WebFingerLink
            {
                Rel = "avatar",
                Type = "image/jpeg",
                Href = profile.Image,
            });
        }

        if (profile.Email != null)
        {
            foreach (var email in profile.Email)
            {
                result.Aliases.Add($"mailto:{email.Email}");
                result.Links.Add(new WebFingerLink
                {
                    Rel = "email",
                    Type = "text/plain",
                    Href = $"mailto:{email.Email}",
                });
            }
        }

        if (profile.Links != null)
        {
            foreach (var link in profile.Links)
            {
                result.Links.Add(new WebFingerLink
                {
                    Rel = "me",
                    Type = "text/html",
                    Href = link.Url,
                    Titles = link.Type == null
                        ? null
                        : new Dictionary<string, string>
                            {
                                { "default", link.Type }
                            }
                });
            }
        }

        return result;
    }
}

//

public class WebFingerResponse
{
    [JsonPropertyName("subject")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subject { get; set; }

    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Aliases { get; set; } = [];

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Properties { get; set; } = new();

    [JsonPropertyName("links")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WebFingerLink>? Links { get; set; } = [];
}

public class WebFingerLink
{
    [JsonPropertyName("rel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rel { get; set; }

    [JsonPropertyName("href")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Href { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("titles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Titles { get; set; }
}
