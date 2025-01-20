using System.Linq;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Controllers.Base.Drive;

public class FileTransferHistoryResponse
{
    public int OriginalRecipientCount { get; set; }
    public PagedResult<RecipientTransferHistoryItem> History { get; set; }

    public RecipientTransferHistoryItem GetHistory(OdinId recipient)
    {
        return this.History?.Results.SingleOrDefault(h => h.Recipient == recipient);
    }
}