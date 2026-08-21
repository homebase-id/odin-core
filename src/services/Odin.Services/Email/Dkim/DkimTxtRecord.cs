using System;
using System.Linq;

namespace Odin.Services.Email.Dkim;

#nullable enable

/// <summary>
/// Parses a DKIM TXT record value ("v=DKIM1; k=ed25519; p=&lt;base64&gt;") as receivers
/// do - tag/value pairs, whitespace-tolerant. Used by the health checks to compare
/// the LIVE DNS value against the stored source-of-truth key.
/// </summary>
public static class DkimTxtRecord
{
    public static bool TryParse(string txtValue, out string kTag, out byte[] publicKey)
    {
        // RFC 6376 defaults k to rsa when absent
        kTag = "rsa";
        publicKey = [];

        if (string.IsNullOrWhiteSpace(txtValue))
        {
            return false;
        }

        string? pValue = null;
        foreach (var tag in txtValue.Split(';'))
        {
            var parts = tag.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var name = parts[0].Trim();
            var value = parts[1].Trim();

            if (name == "k")
            {
                kTag = value;
            }
            else if (name == "p")
            {
                // Whitespace inside the base64 is legal (folded records)
                pValue = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
            }
        }

        if (string.IsNullOrEmpty(pValue))
        {
            return false;
        }

        try
        {
            publicKey = Convert.FromBase64String(pValue);
            return publicKey.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
