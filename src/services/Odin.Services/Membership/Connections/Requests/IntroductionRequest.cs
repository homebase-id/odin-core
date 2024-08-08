using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Membership.Connections.Requests;

public class Introduction
{
    /// <summary>
    /// The identity being introduced
    /// </summary>
    public string Identity { get; init; }

    public UnixTimeUtc Timestamp { get; set; }
}

public class IntroductionRequest
{
    /// <summary>
    /// OdinId of the identity who will make the introduction
    /// </summary>
    public string Requester { get; init; }

    /// <summary>
    /// List of identities receiving the request
    /// </summary>
    public List<string> Recipients { get; init; }
}