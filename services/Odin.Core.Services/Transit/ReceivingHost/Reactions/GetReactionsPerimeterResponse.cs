using System.Collections.Generic;

namespace Odin.Core.Services.Transit.ReceivingHost.Reactions;

public class GetReactionsPerimeterResponse
{
    public List<PerimeterReaction> Reactions { get; set; }

    public int? Cursor { get; set; }
}