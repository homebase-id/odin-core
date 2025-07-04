using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Http;

#nullable enable

// NOTE: this code was mostly created by Claude Opus 4

public static class DynamicHttpClientFactoryExtensions
{
    private const int DefaultMaxRedirects = 5;

    public static async Task<HttpResponseMessage> SendWithRedirectsAsync(
        this IDynamicHttpClientFactory factory,
        HttpRequestMessage request,
        Action<ClientHandlerConfig>? configure = null,
        TimeSpan? timeout = null,
        int maxRedirects = DefaultMaxRedirects,
        HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(request);

        if (maxRedirects < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRedirects), "Max redirects must be non-negative");
        }

        var currentHost = request.RequestUri?.Host ?? throw new ArgumentException("Request URI must have a valid host");
        var currentRequest = request;
        var redirectCount = 0;
        HttpResponseMessage? lastResponse = null;

        try
        {
            while (redirectCount <= maxRedirects)
            {
                var client = factory.CreateClient(currentHost, configure);

                if (timeout.HasValue)
                {
                    client.Timeout = timeout.Value;
                }                
                
                var response = await client.SendAsync(
                    currentRequest,
                    completionOption: httpCompletionOption,
                    cancellationToken: cancellationToken);

                if (!IsRedirect(response))
                {
                    return response;
                }

                if (redirectCount >= maxRedirects)
                {
                    response.Dispose();
                    throw new HttpRequestException(
                        $"Maximum number of redirects ({maxRedirects}) exceeded. " +
                        $"Last redirect from {currentRequest.RequestUri}");
                }

                var locationUri = GetLocationUri(currentRequest, response);
                if (locationUri == null)
                {
                    response.Dispose();
                    throw new HttpRequestException(
                        $"Invalid redirect location in response from {currentHost}. " +
                        $"Status: {response.StatusCode}, Location header: {response.Headers.Location}");
                }

                // Check for redirect loops
                if (locationUri == currentRequest.RequestUri)
                {
                    response.Dispose();
                    throw new HttpRequestException(
                        $"Redirect loop detected: {locationUri} redirects to itself");
                }

                logger?.LogTrace(
                    "Following redirect {Count}/{Max} from {OldUri} to {NewUri}",
                    redirectCount + 1,
                    maxRedirects,
                    currentRequest.RequestUri,
                    locationUri);

                // Prepare for next iteration
                redirectCount++;
                var previousHost = currentHost;
                currentHost = locationUri.Host;

                // Create new request for the redirect
                currentRequest = CreateRedirectRequest(currentRequest, locationUri, response);

                // Dispose the previous response
                lastResponse?.Dispose();
                lastResponse = response;

                // Log if we're switching hosts
                if (!string.Equals(previousHost, currentHost, StringComparison.OrdinalIgnoreCase))
                {
                    logger?.LogTrace(
                        "Redirect crossing hosts from {OldHost} to {NewHost}",
                        previousHost,
                        currentHost);
                }
            }

            // This should not be reached due to the loop structure, but just in case
            throw new HttpRequestException($"Maximum number of redirects ({maxRedirects}) exceeded");
        }
        catch
        {
            // Clean up the last response if we're throwing
            lastResponse?.Dispose();
            throw;
        }
    }

    //

    // Convenience GET method
    public static Task<HttpResponseMessage> GetWithRedirectsAsync(
        this IDynamicHttpClientFactory factory,
        string requestUri,
        Action<ClientHandlerConfig>? configure = null,
        TimeSpan? timeout = null,
        int maxRedirects = DefaultMaxRedirects,
        HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        return factory.SendWithRedirectsAsync(
            request,
            configure,
            timeout,
            maxRedirects,
            httpCompletionOption,
            logger,
            cancellationToken);
    }

    // Convenience POST method
    public static Task<HttpResponseMessage> PostWithRedirectsAsync(
        this IDynamicHttpClientFactory factory,
        string requestUri,
        HttpContent? content,
        Action<ClientHandlerConfig>? configure = null,
        TimeSpan? timeout = null,
        int maxRedirects = DefaultMaxRedirects,
        HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        return factory.SendWithRedirectsAsync(
            request,
            configure,
            timeout,
            maxRedirects,
            httpCompletionOption,
            logger,
            cancellationToken);
    }

    //

    private static bool IsRedirect(HttpResponseMessage response)
    {
        var statusCode = response.StatusCode;
        return statusCode switch
        {
            HttpStatusCode.MovedPermanently => true,        // 301
            HttpStatusCode.Found => true,                   // 302
            HttpStatusCode.SeeOther => true,                // 303
            HttpStatusCode.TemporaryRedirect => true,       // 307
            HttpStatusCode.PermanentRedirect => true,       // 308
            _ => false
        };
    }

    //

    private static Uri? GetLocationUri(HttpRequestMessage request, HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        if (location == null)
        {
            return null;
        }

        if (location.IsAbsoluteUri)
        {
            return location;
        }

        // Handle relative URLs
        if (request.RequestUri != null)
        {
            try
            {
                return new Uri(request.RequestUri, location);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        return null;
    }

    //

    private static HttpRequestMessage CreateRedirectRequest(
        HttpRequestMessage original,
        Uri redirectUri,
        HttpResponseMessage redirectResponse)
    {
        var newMethod = original.Method;
        var includeBody = true;

        // Handle status code specific behavior
        switch (redirectResponse.StatusCode)
        {
            case HttpStatusCode.MovedPermanently:   // 301
            case HttpStatusCode.Found:              // 302
                // RFC 7231: For historical reasons, a user agent MAY change the request method
                // from POST to GET for 301 and 302 responses
                if (original.Method == HttpMethod.Post)
                {
                    newMethod = HttpMethod.Get;
                    includeBody = false;
                }
                break;

            case HttpStatusCode.SeeOther:           // 303
                // RFC 7231: Change method to GET and don't include body
                newMethod = HttpMethod.Get;
                includeBody = false;
                break;

            case HttpStatusCode.TemporaryRedirect:  // 307
            case HttpStatusCode.PermanentRedirect:  // 308
                // RFC 7231: Method and body must not change
                break;
        }

        var newRequest = new HttpRequestMessage(newMethod, redirectUri);

        // Copy headers from original request
        foreach (var header in original.Headers)
        {
            // Skip the Host header as it will be set based on the new URI
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Remove authorization header when redirecting to a different host
            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsSameHost(original.RequestUri, redirectUri))
                {
                    continue;
                }
            }

            newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content if needed
        if (includeBody && original.Content != null)
        {
            // Note: This is a simplified approach. In production, you might need to handle
            // non-seekable streams by buffering the content
            newRequest.Content = original.Content;

            // Copy content headers
            if (original.Content?.Headers != null)
            {
                foreach (var header in original.Content.Headers)
                {
                    newRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        // Note: HttpRequestOptions cannot be generically copied because they use strongly-typed keys.
        // Callers who need specific options preserved across redirects should handle this manually.

        return newRequest;
    }

    //

    private static bool IsSameHost(Uri? uri1, Uri? uri2)
    {
        if (uri1 == null || uri2 == null) return false;

        return string.Equals(uri1.Host, uri2.Host, StringComparison.OrdinalIgnoreCase) &&
               uri1.Port == uri2.Port &&
               string.Equals(uri1.Scheme, uri2.Scheme, StringComparison.OrdinalIgnoreCase);
    }
}
