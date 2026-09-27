using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
/// <remarks>
/// The trust is in the domain, never in whoever runs it. A document proves that the domain which
/// will receive the token calls itself by this name; a phishing domain can publish one too. That is
/// why the consent page shows the name next to the domain and never in its place.
/// </remarks>
public sealed class YouAuthClientMetadata
{
    public static readonly YouAuthClientMetadata Empty = new();

    /// <summary>A name is a label, not a page.</summary>
    public const int MaxNameLength = 64;

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
        YouAuthClientMetadataDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<YouAuthClientMetadataDocument>(json);
        }
        catch (JsonException)
        {
            return Empty;
        }

        if (document == null)
        {
            return Empty;
        }

        var redirectUris = document.RedirectUris?
            .Where(uri => IsOwnHttpsUrl(clientId, uri))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new YouAuthClientMetadata
        {
            Name = CleanName(document.Name),
            Logo = IsOwnHttpsUrl(clientId, document.Logo) ? document.Logo : null,
            // An empty list is no list: publishing no callbacks must not lock every callback out.
            RedirectUris = redirectUris is { Count: > 0 } ? redirectUris : null
        };
    }

    /// <summary>
    /// Whether the domain has said this redirect URI is one of its callbacks: a match on scheme,
    /// host and path, the relying party's own query ignored since it is kept on the way back. True
    /// when it published no list.
    /// </summary>
    public bool AllowsRedirect(Uri redirectUri)
    {
        if (RedirectUris == null)
        {
            return true;
        }

        return RedirectUris.Any(published =>
            Uri.TryCreate(published, UriKind.Absolute, out var allowed)
            && allowed.Scheme == redirectUri.Scheme
            && string.Equals(allowed.Host, redirectUri.Host, StringComparison.OrdinalIgnoreCase)
            && allowed.Port == redirectUri.Port
            && allowed.AbsolutePath == redirectUri.AbsolutePath);
    }

    private static bool IsOwnHttpsUrl(AsciiDomainName clientId, string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && string.Equals(uri.Host, clientId.DomainName, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static string? CleanName(string? name)
    {
        if (name == null)
        {
            return null;
        }

        var visible = new string(name.Where(c => !char.IsControl(c)).ToArray());
        var collapsed = Whitespace.Replace(visible, " ").Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        return collapsed.Length > MaxNameLength ? collapsed[..MaxNameLength] : collapsed;
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

/// <summary>
/// One GET of <see cref="YouAuthDefaults.ClientMetadataPath"/> on the client's own host. A site
/// that is down, absent, redirecting elsewhere, or answering with something other than a small
/// JSON document is a site with no document; none of that may stall or fail a login.
/// </summary>
public sealed class YouAuthClientMetadataFetcher(
    IDynamicHttpClientFactory httpClientFactory,
    ILogger<YouAuthClientMetadataFetcher> logger) : IYouAuthClientMetadataFetcher
{
    /// <summary>A document is a few hundred bytes; anything larger is not read.</summary>
    public const int MaxDocumentBytes = 16 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<YouAuthClientMetadata> FetchAsync(AsciiDomainName clientId, CancellationToken cancellationToken = default)
    {
        var url = $"https://{clientId.DomainName}{YouAuthDefaults.ClientMetadataPath}";

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            var client = httpClientFactory.CreateClient($"{nameof(YouAuthClientMetadataFetcher)}:{clientId.DomainName}");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return YouAuthClientMetadata.Empty;
            }

            // The document must come from the client's own host. A redirect that was followed to
            // another host is somebody else's document.
            var servedBy = response.RequestMessage?.RequestUri?.Host;
            if (servedBy != null && !string.Equals(servedBy, clientId.DomainName, StringComparison.OrdinalIgnoreCase))
            {
                return YouAuthClientMetadata.Empty;
            }

            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return YouAuthClientMetadata.Empty;
            }

            if (response.Content.Headers.ContentLength > MaxDocumentBytes)
            {
                return YouAuthClientMetadata.Empty;
            }

            var body = await ReadAtMostAsync(response, MaxDocumentBytes, timeout.Token);
            return body == null ? YouAuthClientMetadata.Empty : YouAuthClientMetadata.Parse(clientId, body);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            logger.LogDebug(e, "YouAuth: no client metadata from {clientId}: {message}", clientId, e.Message);
            return YouAuthClientMetadata.Empty;
        }
    }

    /// <summary>Reads the body, or null if it turns out to be longer than <paramref name="limit"/>.</summary>
    private static async Task<string?> ReadAtMostAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[limit + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > limit ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }
}

/// <summary>
/// The fetch, cached per client id so every login reads the site's document without every login
/// reading the site. A renamed site or a newly published callback list takes effect within the
/// hour; an absent document is asked for again after a few minutes, not on every login.
/// </summary>
public sealed class YouAuthClientMetadataService(
    IYouAuthClientMetadataFetcher fetcher,
    ITenantLevel2Cache<YouAuthClientMetadataService> cache)
{
    private static readonly TimeSpan FoundFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan AbsentFor = TimeSpan.FromMinutes(5);

    public async Task<YouAuthClientMetadata> GetAsync(AsciiDomainName clientId, CancellationToken cancellationToken = default)
    {
        // An address is not a site that publishes a document, and it is the one client id that
        // could point this server's fetch at something on its own network. Not fetched.
        if (IPAddress.TryParse(clientId.DomainName, out _))
        {
            return YouAuthClientMetadata.Empty;
        }

        var key = $"ClientMetadata:{clientId.DomainName}";
        var cached = await cache.TryGetAsync<YouAuthClientMetadata>(key, cancellationToken);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var metadata = await fetcher.FetchAsync(clientId, cancellationToken);
        await cache.SetAsync(key, metadata, metadata.IsEmpty ? AbsentFor : FoundFor, cancellationToken: cancellationToken);
        return metadata;
    }
}
