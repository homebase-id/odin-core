
namespace Odin.Core.Services.EncryptionKeyService;

public class GetOfflinePublicKeyResponse
{
    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
    public long Expiration { get; set; }
}