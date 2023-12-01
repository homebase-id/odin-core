using System;

namespace Odin.Core.Services.Peer;

/// <summary>
/// Options for notifying a recipient identity server
/// </summary>
public class AppNotificationOptions
{
    public Guid AppId { get; set; }
    public Guid GroupId { get; set; }
}