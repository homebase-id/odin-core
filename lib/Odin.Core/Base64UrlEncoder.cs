using System;

namespace Odin.Core;

public static class Base64UrlEncoder
{
    public static string Encode(byte[] input)
    {
        return Convert.ToBase64String(input).Split('=')[0].Replace('+', '-').Replace('/', '_');
    }

    //

    public static string Encode(string input)
    {
        return Encode(input.ToUtf8ByteArray());
    }

    //

    public static byte[] Decode(string input)
    {
        string base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    //

    public static string DecodeString(string input)
    {
        return Decode(input).ToStringFromUtf8Bytes();
    }

    //
}