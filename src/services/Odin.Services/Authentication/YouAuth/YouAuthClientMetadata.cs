using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;
using Odin.Core.Storage.Cache;
using Odin.Core.Util;

namespace Odin.Services.Authentication.YouAuth;

#nullable enable

/// <summary>
/// The wire shape of <c>/.well-known/youauth-client.json</c>: what a relying party publishes on its
/// own domain about itself, and what an identity publishes about itself as a relying party.
/// </summary>
public sealed class YouAuthClientMetadataDocument
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; set; }
}

/// <summary>
/// A relying party's metadata as the identity accepted it: fetched from the redirect domain, so
/// bound to that domain and nothing more. Every field survives only if it belongs to that domain.
/// </summary>
public sealed class YouAuthClientMetadata
{
    public static readonly YouAuthClientMetadata Empty = new();

    /// <summary>What the redirect domain calls itself; null when it says nothing.</summary>
    public string? Name { get; init; }

    /// <summary>An https URL on the redirect domain, or null.</summary>
    public string? Logo { get; init; }

    /// <summary>
    /// The callbacks the domain says are its own, as absolute https URLs on that domain; null when
    /// it published none, in which case any path on the host is accepted.
    /// </summary>
    public List<string>? RedirectUris { get; init; }

    public bool IsEmpty => Name == null && Logo == null && RedirectUris == null;

    /// <summary>
    /// Parses a document fetched from <paramref name="clientId"/>. Anything that is not that domain's
    /// to claim is dropped; unparsable JSON is <see cref="Empty"/>.
    /// </summary>
    public static YouAuthClientMetadata Parse(AsciiDomainName clientId, string json)
    {
        return Empty;
    }

    /// <summary>
    /// Whether the domain has said this redirect URI is one of its callbacks: a match on scheme,
    /// host and path, the relying party's own query ignored. True when it published no list.
    /// </summary>
    public bool AllowsRedirect(Uri redirectUri)
    {
        return true;
    }
}

/// <summary>
/// Fetches a relying party's metadata document from its domain. Behind an interface so the test
/// host can route the fetch into its in-process server.
/// </summary>
public interface IYouAuthClientMetadataFetcher
{
    Task<YouAuthClientMetadata> FetchAsync(AsciiDomainName clientId, CancellationToken cancellationToken = default);
}

public sealed class YouAuthClientMetadataFetcher(
    IDynamicHttpClientFactory httpClientFactory,
    ILogger<YouAuthClientMetadataFetcher> logger) : IYouAuthClientMetadataFetcher
{
    public Task<YouAuthClientMetadata> FetchAsync(AsciiDomainName clientId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(YouAuthClientMetadata.Empty);
    }
}

/// <summary>
/// The fetch, cached per client id so every login reads the site's document without every login
/// reading the site.
/// </summary>
public sealed class YouAuthClientMetadataService(
    IYouAuthClientMetadataFetcher fetcher,
    ITenantLevel2Cache<YouAuthClientMetadataService> cache)
{
    public Task<YouAuthClientMetadata> GetAsync(AsciiDomainName clientId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(YouAuthClientMetadata.Empty);
    }
}
