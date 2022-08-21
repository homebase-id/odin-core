using System;
using Dawn;

namespace Youverse.Core.SystemStorage;

public abstract class KeyValueStorageBase
{
    protected ByteArrayId PrefixContext(ByteArrayId data, string context)
    {
        if (string.IsNullOrEmpty(context))
        {
            return data;
        }

        Guard.Argument(context, nameof(context))
            .Require(Validators.IsAscii, x => $"{x} must be [A-Za-z0-9]")
            .Require(x => x.Length <= 3, x => $"{x} is too long.  Max is 3 bytes");

        //TODO: change to ByteArrayUtil when we fix dependencies
        static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

        var id = Combine(context.ToUtf8ByteArray(), data.Value);
        return new ByteArrayId(id);
    }
}