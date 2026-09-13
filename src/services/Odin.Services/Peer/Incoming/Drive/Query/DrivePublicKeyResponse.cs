namespace Odin.Services.Peer.Incoming.Drive.Query;

/// <summary>
/// A drive's write-only public key: the half a remote caller seals a deposit to.
/// </summary>
/// <remarks>
/// Only the public half ever leaves the host.  The private half is escrowed under the drive's storage
/// key and is what makes deposit-collection custody equal to existing read access
/// (docs/drive-addressing.md).
/// </remarks>
public class DrivePublicKeyResponse
{
    /// <summary>The public key in JWK form, ready to seal to.</summary>
    public string PublicKeyJwk { get; set; }

    /// <summary>
    /// CRC32C of the public key, so a sealer can label an envelope with the key it used and the
    /// recipient can tell which key an arriving deposit was sealed to.
    /// </summary>
    public uint PublicKeyCrc32 { get; set; }
}
