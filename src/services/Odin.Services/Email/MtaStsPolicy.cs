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
    /// The policy body, from the MX nodes, mode and max_age in <c>Email:TenantMail</c>
    /// </summary>
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
    /// the served policy does (MX nodes, mode or max_age), which is what makes receivers refetch.
    /// No clock dependency.
    /// </summary>
    public static string ComputeId(OdinConfiguration.TenantMailSection tenantMail)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Build(tenantMail)));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
