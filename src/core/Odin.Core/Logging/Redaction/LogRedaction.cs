using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Odin.Core.Logging.CorrelationId;

namespace Odin.Core.Logging.Redaction;

#nullable enable

/// <summary>
/// Keeps the credentials that travel in URLs and headers out of the logs. Headers and query parameters are logged with
/// their values only when they are on an allowlist, so a credential nobody has looked at yet stays out by default.
/// <see cref="Odin.Core.Logging.Redaction.Serilog.UrlRedactionEnricher"/> applies <see cref="Url"/> to every log line.
/// </summary>
public static class LogRedaction
{
    public const string Redacted = "[redacted]";

    // Query parameters whose values may be logged; any other parameter is logged by name only. A credential travels
    // in the query as id (the recovery links' nonce), token, invitation-code, frt, url (a pasted link's own query),
    // and so on, none of which are here.
    private static readonly HashSet<string> LoggableQueryParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "alias", "appId", "archivalStatus", "batchSize", "clientUniqueId", "count", "cursor", "cursorState",
        "directMatchOnly", "discard-mail", "enabled", "extension", "fileId", "fileState", "fileType", "globalTransitId",
        "groupId", "height", "iac", "include-payload", "includeAlias", "includeMetadataHeader", "includeSystemCircle",
        "key", "live", "maxRecords", "notifyRemote", "odinId", "pageNumber", "pageSize", "payloadKey",
        "removeMembers", "ss", "type", "uniqueId", "version", "versionTag", "width", "xfst",
        "client_id", "client_type", "redirect_uri", "error", "error_description",
    };

    // A path segment that follows one of these carries a credential
    private static readonly string[] SegmentsBeforeASecret =
    [
        "/capi/validate/",              // the peer's CAPI session id: {remoteDomain}~{sessionId}
        "/is-valid-invitation-code/",   // the invitation code
    ];

    // Headers whose values may be logged; any other header is logged by name only. Authorization, Cookie, the client
    // auth token headers and Sec-WebSocket-Protocol (which carries the V2 socket's token) all carry credentials.
    private static readonly HashSet<string> LoggableHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept", "Accept-Encoding", "Accept-Language", "Cache-Control", "Connection", "Content-Encoding",
        "Content-Length", "Content-Type", "Expect", "Host", "If-Modified-Since", "If-None-Match", ICorrelationContext.DefaultHeaderName,
        "Origin", "Range", "Sec-WebSocket-Version", "traceparent", "Transfer-Encoding", "Upgrade", "User-Agent",
        "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-ODIN-FILE-SYSTEM-TYPE", "X-Odin-Version",
        "X-Real-IP",
    };

    /// <summary>
    /// A URL or a request path, with or without its query string, with its credentials replaced
    /// </summary>
    public static string Url(string url)
    {
        // Path and Query hand back their input when nothing in it is a credential, so the usual URL is not rebuilt
        var q = url.IndexOf('?');
        if (q < 0)
        {
            return Path(url);
        }

        var path = url[..q];
        var query = url[(q + 1)..];
        var safePath = Path(path);
        var safeQuery = Query(query);
        return ReferenceEquals(safePath, path) && ReferenceEquals(safeQuery, query) ? url : safePath + "?" + safeQuery;
    }

    /// <summary>
    /// A header's value as it may be logged
    /// </summary>
    public static string HeaderValue(string name, string value)
    {
        return LoggableHeaders.Contains(name) ? value : Redacted;
    }

    /// <summary>
    /// Stands in for a secret where log lines must still be told apart (which device a push went to): the first
    /// 8 hex characters of its SHA-256
    /// </summary>
    public static string Fingerprint(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "(none)";
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))[..8];
    }

    private static string Path(string path)
    {
        foreach (var segment in SegmentsBeforeASecret)
        {
            var at = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var start = at + segment.Length;
            var end = path.IndexOf('/', start);
            if (end < 0)
            {
                end = path.Length;
            }

            if (end > start)
            {
                path = path[..start] + Redacted + path[end..];
            }
        }

        return path;
    }

    private static string Query(string query)
    {
        var parameters = query.Split('&');
        var redacted = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var eq = parameters[i].IndexOf('=');
            if (eq > 0 && eq < parameters[i].Length - 1 && !LoggableQueryParameters.Contains(WebUtility.UrlDecode(parameters[i][..eq])))
            {
                parameters[i] = parameters[i][..(eq + 1)] + Redacted;
                redacted = true;
            }
        }

        return redacted ? string.Join('&', parameters) : query;
    }
}
