using System;
using System.Security.Cryptography;
using System.Text;

namespace Odin.Services.Contacts;

/// <summary>
/// Deterministic <c>md5(input)</c>-as-Guid, byte-compatible with odin-js <c>toGuidId</c>. Used for the
/// contact unique id (from an odinId) and for the profile attribute type ids.
/// </summary>
internal static class ContactGuid
{
    public static Guid ToGuidId(string input)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var b = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        return new Guid(b);
    }
}
