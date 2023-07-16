using System;
using System.Collections;
using System.Text;

namespace Odin.Core.Services.Authentication.Owner;

public class RecoveryKeyGenerator
{
    private const string Characters32 = "ABCDEFGHJKLMNPQRTUVWXY34679aefgh"; // Not confusing to read, 0 or O or 1 or I or S or 5

    public static string EncodeKey(byte[] rawKey16)
    {
        if (rawKey16.Length != 16)
            throw new ArgumentException("Key length must be 16 bytes.", "rawKey");

        var bits = new BitArray(rawKey16);

        var stringBuilder = new StringBuilder(rawKey16.Length * 8 / 5);

        int bitIndex = 0;
        while (bitIndex < bits.Length)
        {
            int charIndex = 0;
            for (int i = 0; i < 5; i++)
            {
                if (bitIndex < bits.Length && bits[bitIndex])
                    charIndex |= 1 << i;

                bitIndex++;
            }

            stringBuilder.Append(Characters32[charIndex]);
        }

        return FormatKey(stringBuilder.ToString());
    }

    public static byte[] DecodeKey(string key)
    {
        var cleanedKey = key.Replace("-", "");
        if (cleanedKey.Length != 26)
            throw new ArgumentException("Key length must be 26 characters.", "key");

        var bits = new BitArray(cleanedKey.Length * 5);

        int bitIndex = 0;
        foreach (char c in cleanedKey)
        {
            int charIndex = Characters32.IndexOf(c);
            if (charIndex < 0)
                throw new ArgumentException("Invalid character in key.", "key");

            for (int i = 0; i < 5; i++)
            {
                bits[bitIndex] = (charIndex & (1 << i)) != 0;
                bitIndex++;
            }
        }

        var rawKey = new byte[16];

        // Copy only full bytes to rawKey
        var limitedBits = new BitArray(((cleanedKey.Length * 5) / 8) * 8);
        for (int i = 0; i < 128; i++)
        {
            limitedBits[i] = bits[i];
        }

        limitedBits.CopyTo(rawKey, 0);
        return rawKey;
    }

    private static string FormatKey(string key)
    {
        var parts = new string[key.Length / 4 + 1];

        for (var i = 0; i < key.Length; i += 4)
        {
            parts[i / 4] = key.Substring(i, Math.Min(4, key.Length - i));
        }

        return string.Join("-", parts);
    }
}