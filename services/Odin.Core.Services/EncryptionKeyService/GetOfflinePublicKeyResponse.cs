using System;
using Odin.Core.Time;

namespace Odin.Core.Services.EncryptionKeyService;

public class GetOfflinePublicKeyResponse
{
    public bool IsExpired()
    {
        Int64 t = UnixTimeUtc.Now().seconds;
        if (t > Expiration)
            return true;
        else
            return false;
    }

    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
    public long Expiration { get; set; }
}