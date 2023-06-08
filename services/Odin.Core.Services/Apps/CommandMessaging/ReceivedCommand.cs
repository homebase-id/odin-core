using System;
using System.Collections.Generic;

namespace Odin.Core.Services.Apps.CommandMessaging;

/// <summary>
/// A command received from an app on another identity
/// </summary>
public class ReceivedCommand
{
    public Guid Id { get; set; }
    
    public IEnumerable<Guid> GlobalTransitIdList { get; set; }
    
    /// <summary>
    /// An arbitrary code to be used by the client
    /// </summary>
    public int ClientCode { get; set; }
    
    /// <summary>
    /// An arbitrary json string ot be used by the client
    /// </summary>
    public string ClientJsonMessage { get; set; }
    
    public string Sender { get; set; }
}