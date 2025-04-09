
using Odin.Core.Time;

namespace Odin.Services.EncryptionKeyService;

public class GetOfflinePublicKeyResponse
{
    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
    public UnixTimeUtc Expiration { get; set; }
}