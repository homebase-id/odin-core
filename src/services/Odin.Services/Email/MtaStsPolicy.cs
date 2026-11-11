using System;
using System.Security.Cryptography;
using System.Text;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Email;

/// <summary>
/// The MTA-STS policy served at https://mta-sts.&lt;domain&gt;/.well-known/mta-sts.txt and the
/// matching id for the _mta-sts TXT record. Ships as one unit with the TXT record and the
/// certificate SAN - never the TXT alone (docs/email-dns-plan.md).
/// </summary>
public static class MtaStsPolicy
{
    /// <summary>
    /// "testing" until the mail servers are live and proven. A fleet switches to "enforce" by config
    /// (<c>Email:TenantMail:MtaStsMode</c>), which changes the policy body and thereby the published id, which is what
    /// makes receivers refetch; only once every MX node serves a certificate valid for its name
    /// (docs/stalwart-production-checklist.md)
    /// </summary>
    public const string DefaultMode = "testing";

    public const int DefaultMaxAgeSeconds = 86400;

    /// <summary>The modes RFC 8461 allows</summary>
    public static readonly string[] Modes = ["testing", "enforce", "none"];

    /// <summary>RFC 8461's upper bound on max_age, about one year</summary>
    public const int MaxMaxAgeSeconds = 31557600;

    public static string Build(OdinConfiguration.TenantMailSection tenantMail)
    {
        var sb = new StringBuilder();
        sb.Append("version: STSv1\n");
        sb.Append($"mode: {tenantMail.MtaStsMode}\n");
        foreach (var node in tenantMail.MxNodes)
        {
            sb.Append($"mx: {node}\n");
        }
        sb.Append($"max_age: {tenantMail.MtaStsMaxAgeSeconds}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Deterministic policy id: derived from the whole policy body, so it changes exactly when
    /// the policy changes, its mode and max_age included - no clock dependency.
    /// </summary>
    public static string ComputeId(OdinConfiguration.TenantMailSection tenantMail)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Build(tenantMail)));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
