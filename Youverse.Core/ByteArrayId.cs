namespace Youverse.Core;

// public class ByteArrayId
// {
//     public byte[] Value { get; }
//
//     public ByteArrayId(byte[] id)
//     {
//         AssertIsValid(id);
//         Value = id;
//     }
//
//     public static bool operator ==(ByteArrayId b1, ByteArrayId b2)
//     {
//         return ByteArrayUtil.EquiByteArrayCompare(b1, b2);
//     }
//
//     public static bool operator !=(ByteArrayId b1, ByteArrayId b2)
//     {
//         return !(b1 == b2);
//     }
//
//     public static implicit operator byte[](ByteArrayId id)
//     {
//         return id.Value;
//     }
//
//     public static explicit operator ByteArrayId(byte[] id)
//     {
//         return new ByteArrayId(id);
//     }
//
//     public static implicit operator Guid(ByteArrayId id)
//     {
//         if (CanConvertToGuid(id))
//         {
//             return new Guid(id.Value);
//         }
//
//         throw new YouverseException("Value cannot be converted to a Guid; Check your length");
//     }
//
//     public static explicit operator ByteArrayId(Guid id)
//     {
//         return new ByteArrayId(id.ToByteArray());
//     }
//
//     public static bool CanConvertToGuid(ByteArrayId id)
//     {
//         return id.Value.Length == 16;
//     }
//
//     public static bool IsValid(byte[] id)
//     {
//         bool HasValidLength(byte[] v) => v.Length is >= 8 and <= 16;
//
//         // bool HasValidChars(byte[] v) => v.Any(b => b is <(byte)1 and > (byte)127);
//         bool HasValidChars(byte[] v) => true;
//
//         if (HasValidLength(id) && HasValidChars(id))
//         {
//             return true;
//         }
//
//         return false;
//     }
//
//     public static void AssertIsValid(byte[] id)
//     {
//         if (!IsValid(id))
//         {
//             throw new YouverseException("Invalid id");
//         }
//     }
// }