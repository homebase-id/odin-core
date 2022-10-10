using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Apps.CommandMessaging;

/// <summary>
/// A command received from an app on another identity
/// </summary>
public class ReceivedCommand
{
    public Guid Id { get; set; }
    public TargetDrive Drive { get; set; }
    public IEnumerable<Guid> GlobalTransitIdList { get; set; }
    
    public string ClientJsonMessage { get; set; }
    public IEnumerable<ClientFileHeader> MatchingFiles { get; set; }
}