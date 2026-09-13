using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Odin.Services.Email;

/// <summary>
/// The MTA-STS policy served at https://mta-sts.&lt;domain&gt;/.well-known/mta-sts.txt and the
/// matching id for the _mta-sts TXT record. Ships as one unit with the TXT record and the
/// certificate SAN - never the TXT alone (docs/email-dns-plan.md).
/// </summary>
public static class MtaStsPolicy
{
    // "testing" until the mail servers are live and proven; flipping to "enforce" changes
    // the policy body and thereby the published id, which is what makes receivers refetch
    private const string Mode = "testing";
    private const int MaxAgeSeconds = 86400;

    public static string Build(IEnumerable<string> mxNodes)
    {
        var sb = new StringBuilder();
        sb.Append("version: STSv1\n");
        sb.Append($"mode: {Mode}\n");
        foreach (var node in mxNodes)
        {
            sb.Append($"mx: {node}\n");
        }
        sb.Append($"max_age: {MaxAgeSeconds}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Deterministic policy id: derived from the policy body, so it changes exactly when
    /// the policy changes - no config knob, no clock dependency.
    /// </summary>
    public static string ComputeId(IEnumerable<string> mxNodes)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Build(mxNodes)));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
