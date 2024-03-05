using System.Security.Cryptography;
using System.Text;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

public static class TypeName
{
    public static byte[] Sha1<T>()
    {
        var typeName = typeof(T).FullName;
        if (typeName == null)
        {
            throw new OdinSystemException("Failed to get type name for T");
        }
        var bytes = Encoding.UTF8.GetBytes(typeName);
        return SHA1.HashData(bytes);
    }
}