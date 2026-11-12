using System;
using System.Security.Cryptography;
using System.Text;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Calling;

/// <summary>
/// A short-lived TURN login in the TURN REST scheme, which coturn checks with <c>use-auth-secret</c>:
/// <c>username = "&lt;unix-expiry-seconds&gt;:&lt;identity&gt;"</c>,
/// <c>credential = base64(HMAC-SHA1(sharedSecret, username))</c>. See docs/turn.md.
/// </summary>
public sealed record TurnCredential(string Username, string Credential, UnixTimeUtc ExpiresAt)
{
    /// <summary>
    /// Nothing may follow the identity in the username: coturn strips the expiry and counts its
    /// <c>user-quota</c> per what remains, so another ':' part would give every login its own quota.
    /// </summary>
    public static TurnCredential Create(OdinId identity, string sharedSecret, UnixTimeUtc now, TimeSpan lifetime)
    {
        var expiresAt = now.AddSeconds((long)lifetime.TotalSeconds);
        var username = $"{expiresAt.seconds}:{identity.DomainName}";
        var hmac = HMACSHA1.HashData(Encoding.UTF8.GetBytes(sharedSecret), Encoding.UTF8.GetBytes(username));
        return new TurnCredential(username, Convert.ToBase64String(hmac), new UnixTimeUtc(expiresAt.seconds * 1000));
    }
}
