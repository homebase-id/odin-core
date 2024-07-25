using System;
using System.Collections.Generic;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer;

namespace Odin.Hosting.Controllers.Base.Transit;

public class PeerDeletePayloadRequest
{
    public string Key { get; set; }

    public Guid VersionTag { get; set; }
    
    public FileIdentifier File { get; set; }

    public List<string> Recipients { get; set; }
}

public class PeerDeletePayloadResult
{
    public Dictionary<string, OutboxEnqueuingStatus> RecipientStatus { get; set; }
}