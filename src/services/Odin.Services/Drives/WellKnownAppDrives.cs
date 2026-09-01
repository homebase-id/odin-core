using System;

namespace Odin.Services.Drives;

/// <summary>
/// Drives with a fixed, well-known alias and type, each owned by an app.
///
/// The constants exist so server-side authorization can name the same drive the client declares.
/// A drive created from an app's request keeps the requested alias verbatim
/// (<c>DriveManager.CreateDriveAsync</c> sets <c>DriveId = request.TargetDrive.Alias</c>), and a
/// grant cannot be issued for a drive that does not exist yet
/// (<c>ExchangeGrantService.CreateExchangeGrantAsync</c> resolves with <c>failIfInvalid: true</c>) —
/// so matching on the alias here is exact by construction. That second point is why a drive granted
/// by a built-in app's registration has to be seeded: the registration would otherwise throw.
///
/// Being listed here says nothing about when the drive is created. That is decided by whether its
/// owning app is built-in (<c>BuiltinApps.Builtin</c>), and
/// the seeded set is <c>TenantConfigService.EnsureSystemDrivesExist</c> — which is kept equal to
/// <see cref="BuiltinDrives.Protected"/>, the list that also makes a drive immutable
/// (<c>DriveManager</c> refuses to rename, re-mode or archive anything in it). Seeded and protected
/// are deliberately the same set; a seeded drive the owner could archive is a trap.
///
/// Mirrored by chat-kmp <c>homebase-common/.../config/AppConfig.kt</c>. Change one, change both.
/// DO NOT CHANGE ANY VALUE: the drive already exists on identities that have set the app up, and
/// its alias is its storage id.
/// </summary>
public static class WellKnownAppDrives
{
    /// <summary>
    /// The type shared by PublicPostsChannelDrive and every user-created channel drive.
    /// </summary>
    /// <remarks>
    /// Declared here, not in <c>SystemDriveConstants</c>, so that this type never reads that one. The two
    /// reference each other otherwise -- <c>SystemDrives</c> lists drives declared here -- and a static
    /// initializer cycle resolves by declaration order, silently leaving later fields null.
    /// </remarks>
    public static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");

    /// <summary>
    /// The Email setup app's drive (chat-kmp <c>emailLabeledDrive</c>). Holds the OpenPGP secret
    /// keyrings, the current-key pointer, and the issued app-password credential files.
    /// Read+Write on this drive is the authorization for every <c>/api/v2/mail</c> action.
    /// </summary>
    public static readonly TargetDrive EmailAppDrive = new()
    {
        Alias = Guid.Parse("92bbcad8-3558-417b-9376-9976c086a674"),
        Type = Guid.Parse("37e3480a-4cd7-4a41-a421-ed49866bf07e")
    };
    public static readonly TargetDrive ChatDrive = new()
    {
        Alias = Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11"),
        Type = Guid.Parse("66ea8355ae4155c39b5a719166b510e3")
    };

    public static readonly TargetDrive StickerDrive = new()
    {
        Alias = Guid.Parse("3b9c5f2e-7a41-4d6b-9e0c-8f1a2b3c4d5e"),
        Type = Guid.Parse("a8c64b10-7434-494b-8b8c-a2284bd643c8")
    };

    public static readonly TargetDrive ContactDrive = new()
    {
        Alias = Guid.Parse("2612429d1c3f037282b8d42fb2cc0499"),
        Type = Guid.Parse("70e92f0f94d05f5c7dcd36466094f3a5")
    };

    public static readonly TargetDrive ProfileDrive = new()
    {
        Alias = Guid.Parse("8f12d8c4933813d378488d91ed23b64c"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    public static readonly TargetDrive FeedDrive = new()
    {
        Alias = Guid.Parse("4db49422ebad02e99ab96e9c477d1e08"),
        Type = Guid.Parse("a3227ffba87608beeb24fee9b70d92a6")
    };

    public static readonly TargetDrive PublicPostsChannelDrive = new()
    {
        Alias = Guid.Parse("e8475dc46cb4b6651c2d0dbd0f3aad5f"),
        Type = ChannelDriveType
    };

    public static readonly TargetDrive HomePageConfigDrive = new()
    {
        Alias = Guid.Parse("ec83345af6a747d4404ef8b0f8844caa"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    public static readonly TargetDrive ListsDrive = new()
    {
        Alias = Guid.Parse("a44e7a2651f44a26ad125d7627b35d0e"),
        Type = Guid.Parse("4338d7d2f217486a8790a4982644c15f")
    };

    public static readonly TargetDrive LocationDrive = new()
    {
        Alias = Guid.Parse("2e191a14-8640-4ebc-b0c8-aaac913f6fa8"),
        Type = Guid.Parse("9dbc3bf5-ca24-4d7d-98ca-6933af0ad491")
    };

    public static readonly TargetDrive MailDrive = new()
    {
        Alias = Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0"),
        Type = Guid.Parse("2dfecc40311e41e5a12455e925144202")
    };

    public static readonly TargetDrive MomentsDrive = new()
    {
        Alias = Guid.Parse("a85f8562-6c74-4947-896b-619812cafccc"),
        Type = Guid.Parse("4338d7d2-f217-486a-8790-a4982644c15f")
    };

    public static readonly TargetDrive ShardRecoveryDrive = new()
    {
        Alias = Guid.Parse("46242d0d67604b2aa683f05cd48d4aef"),
        Type = Guid.Parse("43138ae90206480b9ff493580ca147ee")
    };

    public static readonly TargetDrive WalletDrive = new()
    {
        Alias = Guid.Parse("a6f991e214b11c8c9796f664e1ec0cac"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    /// <summary>The Community app's drive.</summary>
    public static readonly TargetDrive CommunityDrive = new()
    {
        Alias = Guid.Parse("3e5de26f8fa343c1975ad0dd2aa8564c"),
        Type = Guid.Parse("93a6e08d14d9479e8d99bae4e5348a16")
    };


    /// <summary>The Photo app's library drive.</summary>
    public static readonly TargetDrive PhotoLibraryDrive = new()
    {
        Alias = Guid.Parse("6483b7b1f71bd43eb6896c86148668cc"),
        Type = Guid.Parse("2af68fe72fb84896f39f97c59d60813a")
    };

    /// <summary>The Vault app's drive.</summary>
    /// <summary>The Vault app's drive (chat-kmp <c>vaultLabeledDrive</c>).</summary>
    /// <remarks>
    /// These are chat-kmp's values, and they are what identities actually have -- odin-core named a
    /// different drive entirely, so the v13 -&gt; v14 stamp would have looked for one nobody has.
    /// <para>
    /// Two things about them are deliberate rather than overlooked.  The alias is the RFC 4122 example
    /// uuid, and the type is <see cref="ContactDrive"/>'s: chat-kmp calls the pair a placeholder until
    /// the server feature ships.  So this is the only drive whose type is shared -- a query by that type
    /// returns Contacts and Vault both, and the type slug is one name for two drives.  Replace both here
    /// and in chat-kmp together when the real guids land.
    /// </para>
    /// </remarks>
    public static readonly TargetDrive VaultDrive = new()
    {
        Alias = Guid.Parse("f47ac10b58cc4372a5670e02b2c3d479"),
        Type = Guid.Parse("70e92f0f94d05f5c7dcd36466094f3a5")
    };

    /// <summary>The Webdrop app's drive (chat-kmp <c>webDropLabeledDrive</c>).</summary>
    public static readonly TargetDrive WebDropDrive = new()
    {
        Alias = Guid.Parse("6d1711af-8b93-43ef-b798-b84d51f25828"),
        Type = Guid.Parse("edee430a-73d4-49ae-a9ae-2d3091957702")
    };
}
