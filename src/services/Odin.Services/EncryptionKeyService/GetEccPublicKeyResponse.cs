namespace Odin.Services.EncryptionKeyService;

public class GetEccPublicKeyResponse
{
    public uint CRC32c { get; set; }
    public string PublicKeyJwkBase64Url { get; set; }
    public long Expiration { get; set; }
}