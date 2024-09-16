using System.Collections.Generic;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Membership.Connections.Requests;

public class IntroductionResult
{
    public Dictionary<string, bool> RecipientStatus { get; set; } = new();
}

public class Introduction
{
    /// <summary>
    /// The identities being introduced
    /// </summary>
    public List<string> Identities { get; init; }

    public UnixTimeUtc Timestamp { get; set; }
    public string Message { get; set; }
}

public class IntroductionGroup
{
    public string Message { get; init; }

    /// <summary>
    /// List of identities receiving the request
    /// </summary>
    public List<string> Recipients { get; init; }

    public SignatureData Signature { get; set; }
}

public class IdentityIntroduction
{
    public OdinId Identity { get; init; }
    public string Message { get; init; }
    public OdinId IntroducerOdinId { get; init; }
    
    public UnixTimeUtc LastProcessed { get; init; }
}