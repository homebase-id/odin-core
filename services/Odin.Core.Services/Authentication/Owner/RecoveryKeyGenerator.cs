using System;
using System.Collections;
using System.Text;

namespace Odin.Core.Services.Authentication.Owner;

public class RecoveryKeyGenerator
{
    public const string Characters = "ABCDEFGHJKLMNPQRTUVWXY34679abcdef";

    public static string EncodeKey(byte[] rawKey)
    {
        if (rawKey.Length != 16)
            throw new ArgumentException("Key length must be 16 bytes.", "rawKey");

        var bits = new BitArray(rawKey);

        var stringBuilder = new StringBuilder(rawKey.Length * 8 / 5);

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

            stringBuilder.Append(Characters[charIndex]);
        }

        return FormatKey(stringBuilder.ToString());
    }

    public static byte[] DecodeKey(string key)
    {
        var cleanedKey = key.Replace("-", "");
        if (cleanedKey.Length != 32)
            throw new ArgumentException("Key length must be 32 characters.", "key");

        var bits = new BitArray(cleanedKey.Length * 5);

        int bitIndex = 0;
        foreach (char c in cleanedKey)
        {
            int charIndex = Characters.IndexOf(c);
            if (charIndex < 0)
                throw new ArgumentException("Invalid character in key.", "key");

            for (int i = 0; i < 5; i++)
            {
                bits[bitIndex] = (charIndex & (1 << i)) != 0;
                bitIndex++;
            }
        }

        var rawKey = new byte[16];
        bits.CopyTo(rawKey, 0);
        return rawKey;
    }

    private static string FormatKey(string key)
    {
        var parts = new string[key.Length / 4];

        for (var i = 0; i < key.Length; i += 4)
        {
            parts[i / 4] = key.Substring(i, 4);
        }

        return string.Join("-", parts);
    }
}