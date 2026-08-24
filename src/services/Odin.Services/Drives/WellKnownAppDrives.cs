using System;

namespace Odin.Services.Drives;

/// <summary>
/// Drives with a fixed, well-known alias/type that are deliberately NOT system drives: the server
/// never creates them, they are absent from <see cref="SystemDriveConstants.SystemDrives"/>, and
/// they come into existence only when the owner approves an app's drive request in the owner
/// console (the extend-permissions flow).
///
/// The constants exist so server-side authorization can name the same drive the client declares.
/// A drive created from an app's request keeps the requested alias verbatim
/// (<c>DriveManager.CreateDriveAsync</c> sets <c>DriveId = request.TargetDrive.Alias</c>), and a
/// grant cannot be issued for a drive that does not exist yet
/// (<c>ExchangeGrantService.CreateExchangeGrantAsync</c> resolves with <c>failIfInvalid: true</c>) —
/// so matching on the alias here is exact by construction.
///
/// Mirrored by chat-kmp <c>homebase-common/.../config/AppConfig.kt</c>. Change one, change both.
/// DO NOT CHANGE ANY VALUE: the drive already exists on identities that have set the app up, and
/// its alias is its storage id.
/// </summary>
public static class WellKnownAppDrives
{
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
}
