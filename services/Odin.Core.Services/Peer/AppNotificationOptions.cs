using System;

namespace Odin.Core.Services.Peer;

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
}