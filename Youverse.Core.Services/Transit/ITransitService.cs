using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitService
    {
        Task<TransferResult> SendBatchNow(IEnumerable<TransferQueueItem> queuedItems);
        Task<TransferResult> SendBatchNow(RecipientList recipients, TransferSpec spec);
        Task<TransferResult> StartDataTransfer(RecipientList recipients, TransferSpec spec);
        Task<SendResult> SendNow(string recipient, TransferSpec spec);
    }
}