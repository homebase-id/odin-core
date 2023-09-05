using System.Collections.Generic;

namespace Odin.Core.Services.Peer.ReceivingHost.Reactions;

public class GetReactionsPerimeterResponse
{
    public List<PerimeterReaction> Reactions { get; set; }

    public int? Cursor { get; set; }
}