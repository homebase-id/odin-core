using System.Collections.Generic;

namespace Youverse.Core.Services.Transit.ReceivingHost.Reactions;

public class GetReactionsPerimeterResponse
{
    public List<PerimeterReaction> Reactions { get; set; }

    public int? Cursor { get; set; }
}