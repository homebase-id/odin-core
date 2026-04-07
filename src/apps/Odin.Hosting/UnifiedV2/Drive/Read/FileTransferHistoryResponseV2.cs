using System.Linq;
using Odin.Core;
using Odin.Core.Identity;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class FileTransferHistoryResponseV2
{
    public int OriginalRecipientCount { get; set; }
    public PagedResult<RecipientTransferHistoryItemV2> History { get; set; }

    public RecipientTransferHistoryItemV2 GetHistoryItem(OdinId recipient)
    {
        return this.History?.Results.SingleOrDefault(h => h.Recipient == recipient);
    }
}
