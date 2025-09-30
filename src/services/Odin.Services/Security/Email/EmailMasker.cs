#nullable enable
using System;
using System.Linq;
using System.Net.Mail;

namespace Odin.Services.Security.Email;

public static class EmailMasker
{
    /// <summary>
    /// Masks an email address for display:
    /// - Local part: keep first and last char, mask the middle with '*'
    ///   (for very short names, keeps as much as possible).
    /// - Preserves "+tag" by showing "+…"
    /// - Domain: keep TLD fully; mask the second-level label (keep first/last);
    ///   collapse any left subdomains to "*".
    /// Returns the original input if it doesn't look like a valid email.
    /// </summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;

        try
        {
            // Validate/normalize using MailAddress (throws if invalid)
            var addr = new MailAddress(email);
            var original = addr.Address; // normalized
            var atIndex = original.LastIndexOf('@');
            if (atIndex <= 0 || atIndex == original.Length - 1) return email;

            var local = original[..atIndex];
            var domain = original[(atIndex + 1)..];

            // Handle +tag in local part
            string tag = "";
            var plusIndex = local.IndexOf('+');
            if (plusIndex >= 0)
            {
                tag = "+…"; // show presence of tag without content
                local = local[..plusIndex]; // mask only the base
            }

            var maskedLocal = MaskMiddle(local);

            // Domain: split into labels
            var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (labels.Length == 0) return email;

            // Always keep TLD (last label) as-is
            var tld = labels[^1];

            // Second-level domain (SLD) just before TLD, if present
            string? sld = labels.Length >= 2 ? labels[^2] : null;
            string maskedSld = sld is null ? "" : MaskMiddle(sld);

            // Any subdomains to the left of SLD become "*"
            var leftSubsCount = Math.Max(0, labels.Length - 2);
            var leftSubs = Enumerable.Repeat("*", leftSubsCount);

            var maskedDomainParts = (leftSubsCount > 0 ? leftSubs : Array.Empty<string>())
                .Concat(sld is null ? Array.Empty<string>() : new[] { maskedSld })
                .Concat(new[] { tld });

            var maskedDomain = string.Join('.', maskedDomainParts);

            return $"{maskedLocal}{tag}@{maskedDomain}";
        }
        catch
        {
            // If parsing fails, fall back to a light heuristic
            return FallbackMask(email);
        }

        // Local helper to mask the middle of a token: keep first and last char
        static string MaskMiddle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length == 1) return s; // "a" -> "a"
            if (s.Length == 2) return $"{s[0]}*"; // "ab" -> "a*"
            return $"{s[0]}{new string('*', s.Length - 2)}{s[^1]}"; // "alex" -> "a**x"
        }

        static string FallbackMask(string raw)
        {
            var at = raw.LastIndexOf('@');
            if (at <= 0) return raw.Length <= 2 ? raw : $"{raw[0]}***";
            var local = raw[..at];
            var domain = raw[(at + 1)..];
            return $"{MaskMiddle(local)}@{domain}";
        }
    }
}