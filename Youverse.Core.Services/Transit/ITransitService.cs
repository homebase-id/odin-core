using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitService
    {
        Task<TransferResult> SendBatchNow(IEnumerable<TransferQueueItem> queuedItems);
        Task<TransferResult> SendBatchNow(RecipientList recipients, TransferEnvelope envelope);
        Task<TransferResult> StartDataTransfer(RecipientList recipients, TransferEnvelope envelope);
        Task<SendResult> SendNow(string recipient, TransferEnvelope envelope);
    }
}