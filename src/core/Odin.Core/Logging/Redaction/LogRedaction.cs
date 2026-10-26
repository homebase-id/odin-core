using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Odin.Core.Logging.Redaction;

#nullable enable

/// <summary>
/// Keeps the credentials that travel in a request's URL and headers out of the logs. One set of rules for every
/// place that logs a request: the request logger, the exception middleware, and the RequestPath attribute that
/// ASP.NET Core puts on every log line of a request (see <see cref="Odin.Core.Logging.Redaction.Serilog.RequestPathRedactionEnricher"/>).
/// </summary>
public static class LogRedaction
{
    public const string Redacted = "[redacted]";

    // A query parameter is a credential when its name contains one of these (token, invitation-code, ...)
    private static readonly string[] SecretQueryNameParts = ["token", "secret", "password", "invitation"];

    // ... or is one of these: the first-run token
    private static readonly HashSet<string> SecretQueryNames = new(StringComparer.OrdinalIgnoreCase) { "frt" };

    // A path segment is a credential when it follows one of these: GET .../is-valid-invitation-code/{code}
    private static readonly HashSet<string> SegmentsBeforeASecret = new(StringComparer.OrdinalIgnoreCase)
    {
        "is-valid-invitation-code"
    };

    // Headers whose values may be logged. Any other header is logged by name only: Authorization, Cookie, the
    // client auth token headers and Sec-WebSocket-Protocol (which carries the V2 socket's token) all carry
    // credentials, and a header nobody has looked at yet must not leak by default.
    private static readonly HashSet<string> LoggableHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Accept-Encoding",
        "Accept-Language",
        "Cache-Control",
        "Connection",
        "Content-Encoding",
        "Content-Length",
        "Content-Type",
        "Expect",
        "Host",
        "If-Modified-Since",
        "If-None-Match",
        "Odin-Correlation-Id",
        "Origin",
        "Range",
        "Sec-WebSocket-Version",
        "traceparent",
        "Transfer-Encoding",
        "Upgrade",
        "User-Agent",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-ODIN-FILE-SYSTEM-TYPE",
        "X-Odin-Version",
        "X-Real-IP",
    };

    /// <summary>
    /// A request path, with or without its query string, with the values of its credentials replaced
    /// </summary>
    public static string Url(string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery))
        {
            return pathAndQuery;
        }

        var q = pathAndQuery.IndexOf('?');
        if (q < 0)
        {
            return Path(pathAndQuery);
        }

        return Path(pathAndQuery[..q]) + "?" + Query(pathAndQuery[(q + 1)..]);
    }

    /// <summary>
    /// A header's value as it may be logged
    /// </summary>
    public static string HeaderValue(string name, string value)
    {
        return LoggableHeaders.Contains(name) ? value : Redacted;
    }

    private static string Path(string path)
    {
        var segments = path.Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            if (segments[i].Length > 0 && SegmentsBeforeASecret.Contains(segments[i - 1]))
            {
                segments[i] = Redacted;
            }
        }

        return string.Join('/', segments);
    }

    private static string Query(string query)
    {
        var parameters = query.Split('&');
        for (var i = 0; i < parameters.Length; i++)
        {
            var eq = parameters[i].IndexOf('=');
            if (eq > 0 && IsSecretQueryName(WebUtility.UrlDecode(parameters[i][..eq])))
            {
                parameters[i] = parameters[i][..(eq + 1)] + Redacted;
            }
        }

        return string.Join('&', parameters);
    }

    private static bool IsSecretQueryName(string name)
    {
        return SecretQueryNames.Contains(name) ||
               SecretQueryNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}
