using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps.CommandMessaging;

public class CommandMessage
{
    public List<string> Recipients { get; set; }

    /// <summary>
    /// List of files which can be affected by this command
    /// </summary>
    public List<Guid> GlobalTransitIdList { get; set; }

    /// <summary>
    /// Arbitrary code set by the caller indicating the command.
    /// </summary>
    public int Code { get; set; }
    
    /// <summary>
    /// Arbitrary json  string set by the caller to convey any info needed.
    /// </summary>
    public string JsonMessage { get; set; }

    public bool IsValid()
    {
        if (string.IsNullOrEmpty(JsonMessage) || string.IsNullOrWhiteSpace(JsonMessage))
        {
            return false;
        }

        if (!this.Recipients?.Any() ?? false)
        {
            return false;
        }

        return true;
    }
}

public class CommandMessageResult
{
    public CommandMessageResult()
    {
        this.RecipientStatus = new();
    }

    public Dictionary<string, TransferStatus> RecipientStatus { get; set; }
}