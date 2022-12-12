using System;

namespace Youverse.Core.Services.EncryptionKeyService;

public class GetOfflinePublicKeyResponse
{
    public bool IsExpired()
    {
        UInt64 t = UnixTimeUtc.Now().seconds;
        if (t > Expiration)
            return true;
        else
            return false;
    }

    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
    public ulong Expiration { get; set; }
}