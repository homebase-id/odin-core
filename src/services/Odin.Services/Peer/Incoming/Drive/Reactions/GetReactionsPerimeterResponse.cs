using System.Collections.Generic;

namespace Odin.Services.Peer.Incoming.Reactions;

public class GetReactionsPerimeterResponse
{
    public List<PerimeterReaction> Reactions { get; set; }

    public int? Cursor { get; set; }
}