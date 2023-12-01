using System;

namespace Odin.Core.Services.Peer;

/// <summary>
/// Options for notifying a recipient identity server
/// </summary>
public class AppNotificationOptions
{
    public Guid AppId { get; set; }

    public Guid GroupId { get; set; }

    public Guid TagId { get; set; }
    
    /// <summary>
    /// Do not play a sound or vibrate the phone
    /// </summary>
    public bool Silent { get; set; }
}