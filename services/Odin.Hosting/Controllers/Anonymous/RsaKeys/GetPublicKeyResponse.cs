namespace Odin.Hosting.Controllers.Anonymous.RsaKeys;

public class GetPublicKeyResponse
{
    public byte[] PublicKey { get; set; }
    public uint Crc32 { get; set; }
}
