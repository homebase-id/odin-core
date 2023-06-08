using System;
using System.Collections.Generic;

namespace Odin.Core.Services.Apps.CommandMessaging;

/// <summary>
/// Defines the data transferred from the command message service to the recipient's command message service
/// </summary>
public class CommandTransferMessage
{
    /// <summary>
    /// The Command.JsonMessage send by the client
    /// </summary>
    public string ClientJsonMessage { get; set; }

    /// <summary>
    /// Th affected transit Ids as specified by the client
    /// </summary>
    public List<Guid> GlobalTransitIdList { get; set; }
}