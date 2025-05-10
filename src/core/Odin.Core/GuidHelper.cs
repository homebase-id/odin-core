using System;

namespace Odin.Core;

public static class GuidHelper
{
    // The Grokster was here!
    public static (char, char) GetLastTwoNibbles(Guid guid)
    {
        // Convert the GUID to a 16-byte array
        var bytes = guid.ToByteArray();

        // Get the last byte (index 15)
        var lastByte = bytes[15];

        // Extract the high nibble (bits 7-4) by shifting right 4 bits and masking with 0x0F
        var highNibble = (byte)((lastByte >> 4) & 0x0F);

        // Extract the low nibble (bits 3-0) by masking with 0x0F
        var lowNibble = (byte)(lastByte & 0x0F);

        // Define a string of hexadecimal digits for lookup
        const string hexDigits = "0123456789abcdef";

        // Get the hex character for each nibble
        var high = hexDigits[highNibble];
        var low = hexDigits[lowNibble];

        return (high,low);
    }
}


