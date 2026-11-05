using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Query;

/// <summary>
/// A drive's write-only public key: the half a remote caller seals a deposit to.
/// </summary>
/// <remarks>
/// Only the public half ever leaves the host.  The private half is escrowed under the drive's storage
/// key and is what makes deposit-collection custody equal to existing read access
/// (docs/security-model.md, Drive addressing).
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

    /// <summary>
    /// Whether the drive is zero knowledge: its owner's browser alone opens the private half.  When it
    /// isn't, the owner's server can open whatever is sealed to it.
    /// </summary>
    public bool IsZeroKnowledge { get; set; }

    /// <summary>
    /// Who holds the drive's key (docs/zero-knowledge-drives.md), so a sealer knows what the public key is bound
    /// to: on a <see cref="DriveSharingModel.Collaborative"/> drive every member holds it and can open what is
    /// sealed to it, so a circle's share key must not be sealed there (docs/selective-share-plan.md).  Cached with
    /// the key.
    /// </summary>
    public DriveSharingModel SharingModel { get; set; }
}
