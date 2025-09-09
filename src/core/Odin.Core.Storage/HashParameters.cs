using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Text;
using Odin.Core.Time;

namespace Odin.Core.Storage;

#nullable enable

public static class HashParameters
{
    public static string Calculate(params object[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var param in parameters)
        {
            WriteToStream(writer, param);
        }
        var bytes = stream.ToArray();
        Span<byte> hashBytes = stackalloc byte[8]; // XXH3 is 64-bit (8 bytes).
        XxHash3.Hash(bytes, hashBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    //

    private static void WriteToStream(BinaryWriter writer, object? value)
    {
        if (value == null)
        {
            writer.Write((byte)0); // Null marker.
            return;
        }

        switch (value)
        {
            //
            // System types
            //
            case bool b:
                writer.Write(b);
                break;
            case byte b:
                writer.Write(b);
                break;
            case sbyte sb:
                writer.Write(sb);
                break;
            case short s:
                writer.Write(s);
                break;
            case ushort us:
                writer.Write(us);
                break;
            case int i:
                writer.Write(i);
                break;
            case uint ui:
                writer.Write(ui);
                break;
            case long l:
                writer.Write(l);
                break;
            case ulong ul:
                writer.Write(ul);
                break;
            case float f:
                writer.Write(f);
                break;
            case double d:
                writer.Write(d);
                break;
            case decimal dec:
                var bits = decimal.GetBits(dec);
                foreach (var bit in bits) writer.Write(bit);
                break;
            case char c:
                writer.Write(c);
                break;
            case string str:
                var strBytes = Encoding.UTF8.GetBytes(str);
                writer.Write(strBytes.Length); // Length prefix for determinism.
                writer.Write(strBytes);
                break;
            case byte[] bytes:
                writer.Write(bytes.Length); // Length prefix.
                writer.Write(bytes);
                break;
            case Enum e:
                // Hash the underlying integer value.
                var underlying = Convert.ChangeType(e, e.GetTypeCode());
                WriteToStream(writer, underlying);
                break;
            case Guid guid:
                writer.Write(guid.ToByteArray());
                break;
            case List<int> listInt:
                writer.Write(listInt.Count); // Length prefix.
                foreach (var item in listInt)
                {
                    WriteToStream(writer, item);
                }
                break;
            case List<Guid> listGuid:
                writer.Write(listGuid.Count); // Length prefix.
                foreach (var item in listGuid)
                {
                    WriteToStream(writer, item);
                }
                break;
            case List<string> listString:
                writer.Write(listString.Count); // Length prefix.
                foreach (var item in listString)
                {
                    WriteToStream(writer, item);
                }
                break;

            //
            // Odin types
            //
            case UnixTimeUtc utc:
                writer.Write(utc.milliseconds);
                break;
            case QueryBatchCursor qb:
                WriteToStream(writer, qb.ToJson());
                break;
            case IntRange ir:
                WriteToStream(writer, ir.Start);
                WriteToStream(writer, ir.End);
                break;
            case UnixTimeUtcRange utr:
                WriteToStream(writer, utr.Start);
                WriteToStream(writer, utr.End);
                break;

            //
            // OH NO!
            //
            default:
                throw new NotSupportedException(
                    $"Unsupported type for hashing: {value.GetType().FullName}. " +
                     "You should probably do something about that above");
        }
    }
}
