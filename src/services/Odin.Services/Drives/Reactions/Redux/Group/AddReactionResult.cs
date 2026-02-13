using System.Collections.Generic;
using Odin.Services.Peer;

namespace Odin.Services.Drives.Reactions.Redux.Group;

public class AddReactionResult
{
    public Dictionary<string, TransferStatus> RecipientStatus { get; set; } = new();
}

public class DeleteReactionResult
{
    public Dictionary<string, TransferStatus> RecipientStatus { get; set; } = new();
}