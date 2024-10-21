using System;

namespace Odin.Services.EncryptionKeyService;

public class GetPublicKeyResponse
{
    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
    
    public long Expiration { get; set; }

}