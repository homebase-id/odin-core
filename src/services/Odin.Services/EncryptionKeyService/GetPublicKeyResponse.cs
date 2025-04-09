using Odin.Core.Time;
using System;

namespace Odin.Services.EncryptionKeyService;

public class GetPublicKeyResponse
{
    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
    
    public UnixTimeUtc Expiration { get; set; }

}