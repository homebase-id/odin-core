using System;
using Microsoft.AspNetCore.WebUtilities;

namespace Odin.Core;

public static class Base64UrlEncoder
{
    public static string Encode(byte[] input)
    {
        return WebEncoders.Base64UrlEncode(input);
    }

    //

    public static string Encode(string input)
    {
        return Encode(input.ToUtf8ByteArray());
    }

    //

    public static byte[] Decode(string input)
    {
        return WebEncoders.Base64UrlDecode(input);
    }

    //

    public static string DecodeString(string input)
    {
        return Decode(input).ToStringFromUtf8Bytes();
    }

    //
}