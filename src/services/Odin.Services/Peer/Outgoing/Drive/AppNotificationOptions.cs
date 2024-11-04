using System;

namespace Odin.Services.Peer.Outgoing.Drive;

/// <summary>
/// Options for notifying a recipient identity server
/// </summary>
public class AppNotificationOptions
{
    public Guid AppId { get; set; }

    public Guid TypeId { get; set; }

    /// <summary>
    /// An app-specific identifier
    /// </summary>
    public Guid TagId { get; set; }

    /// <summary>
    /// Do not play a sound or vibrate the phone
    /// </summary>
    public bool Silent { get; set; }

    public string UnEncryptedMessage { get; set; }

    // /// <summary>
    // /// Additional data added by parts of the system depending on TypeId
    // /// </summary>
    // public string UnEncryptedJson { get; set; }

    // public AppNotificationOptions Redacted()
    // {
    //     return new AppNotificationOptions
    //     {
    //         AppId = this.AppId,
    //         TypeId = this.TypeId,
    //         TagId = this.TagId,
    //         Silent = this.Silent,
    //         UnEncryptedMessage = this.UnEncryptedJson,
    //         UnEncryptedJson = null // never send this over push notifications
    //     };
    // }
}