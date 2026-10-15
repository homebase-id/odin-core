using System.Security.Cryptography;
using System.Text;

namespace Odin.SetupHelper.InvitationCodes;

/// <summary>
/// Invitation codes are 12 Crockford base32 characters, shown in groups of four (K7QX-M2PD-9FHW).
/// They are stored without dashes; input is case-insensitive and forgives the letters Crockford
/// leaves out (O reads as 0, I and L as 1).
/// </summary>
public static class InvitationCodeFormat
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 12;
    private const int GroupSize = 4;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars);
    }

    /// <summary>
    /// The stored form of what someone typed, or null if it cannot be a code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sb = new StringBuilder(Length);
        foreach (var c in input.ToUpperInvariant())
        {
            switch (c)
            {
                case '-' or ' ':
                    continue;
                case 'O':
                    sb.Append('0');
                    break;
                case 'I' or 'L':
                    sb.Append('1');
                    break;
                default:
                    if (!Alphabet.Contains(c))
                    {
                        return null;
                    }
                    sb.Append(c);
                    break;
            }
        }

        return sb.Length == Length ? sb.ToString() : null;
    }

    public static string Display(string code)
    {
        var sb = new StringBuilder(code.Length + code.Length / GroupSize);
        for (var i = 0; i < code.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                sb.Append('-');
            }
            sb.Append(code[i]);
        }
        return sb.ToString();
    }
}
