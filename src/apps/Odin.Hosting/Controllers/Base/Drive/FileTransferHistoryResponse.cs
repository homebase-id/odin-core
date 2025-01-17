using Odin.Core;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Controllers.Base.Drive;

public class FileTransferHistoryResponse
{
    public int OriginalRecipientCount { get; set; }
    public PagedResult<RecipientTransferHistoryItem> History { get; set; }
}