using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable enable

namespace Odin.Services.Email.Relay;

/// <summary>
/// Wire models for SMTP2GO's /domain/* endpoints. Field names are theirs (snake_case) and are
/// mapped explicitly rather than by a naming policy, so a rename on their side surfaces as a
/// null here and fails the mapping test rather than silently emptying a DNS record.
///
/// Shapes captured from live responses, not from documentation - the docs example showed empty
/// tracker values that the real API populates differently.
/// </summary>
public class Smtp2GoDomainResponse
{
    [JsonPropertyName("request_id")] public string RequestId { get; init; } = "";
    [JsonPropertyName("data")] public Smtp2GoDomainData Data { get; init; } = new();
}

public class Smtp2GoDomainData
{
    [JsonPropertyName("domains")] public List<Smtp2GoDomainEntry> Domains { get; init; } = [];

    /// <summary>Populated instead of <see cref="Domains"/> on a 400. See <see cref="ErrorCode"/>.</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>
    /// Generic across every failure (E_ApiResponseCodes.API_EXCEPTION), so it cannot distinguish
    /// "already exists" from anything else. That is why onboarding checks with /domain/view
    /// rather than adding and interpreting the error.
    /// </summary>
    [JsonPropertyName("error_code")] public string? ErrorCode { get; init; }
}

public class Smtp2GoDomainEntry
{
    [JsonPropertyName("domain")] public Smtp2GoDomain Domain { get; init; } = new();
    [JsonPropertyName("trackers")] public List<Smtp2GoTracker> Trackers { get; init; } = [];
}

public class Smtp2GoDomain
{
    [JsonPropertyName("fulldomain")] public string FullDomain { get; init; } = "";

    /// <summary>e.g. "s934313" - the label before "._domainkey".</summary>
    [JsonPropertyName("dkim_selector")] public string DkimSelector { get; init; } = "";

    /// <summary>
    /// What must be published. NOT the same as <see cref="DkimValue"/>: /domain/add echoes the
    /// target into both, but /domain/view leaves *_value EMPTY until the record is verified -
    /// it reports what they can currently see, not what they want. Reading the wrong one
    /// publishes a CNAME with an empty target.
    /// </summary>
    [JsonPropertyName("dkim_expected")] public string DkimExpected { get; init; } = "";

    /// <summary>What the relay currently observes. Empty until verified — see <see cref="DkimExpected"/>.</summary>
    [JsonPropertyName("dkim_value")] public string DkimValue { get; init; } = "";

    [JsonPropertyName("dkim_verified")] public bool DkimVerified { get; init; }

    /// <summary>Human-readable diagnostics when unverified - worth surfacing verbatim.</summary>
    [JsonPropertyName("dkim_status")] public string DkimStatus { get; init; } = "";

    /// <summary>e.g. "em934313" - a bare label on the tenant domain, NOT under _domainkey.</summary>
    [JsonPropertyName("rpath_selector")] public string RpathSelector { get; init; } = "";

    /// <summary>What must be published. See <see cref="DkimExpected"/> for why this is not *_value.</summary>
    [JsonPropertyName("rpath_expected")] public string RpathExpected { get; init; } = "";

    [JsonPropertyName("rpath_value")] public string RpathValue { get; init; } = "";
    [JsonPropertyName("rpath_verified")] public bool RpathVerified { get; init; }
    [JsonPropertyName("rpath_status")] public string RpathStatus { get; init; } = "";
}

public class Smtp2GoTracker
{
    [JsonPropertyName("fulldomain")] public string FullDomain { get; init; } = "";
    [JsonPropertyName("cname_expected")] public string CnameExpected { get; init; } = "";
    [JsonPropertyName("cname_value")] public string CnameValue { get; init; } = "";
    [JsonPropertyName("cname_verified")] public bool CnameVerified { get; init; }
    [JsonPropertyName("cname_status")] public string CnameStatus { get; init; } = "";

    /// <summary>
    /// False unless tracking was asked for. /domain/verify probes the tracker name regardless
    /// and reports it unverified, so a disabled tracker must never be read as a failure.
    /// </summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
}
