using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Manages the files and state information of an incoming transfer
    /// </summary>
    public interface ITransitPerimeterTransferStateService
    {
        /// <summary>
        /// Creates a tracker for an coming file
        /// </summary>
        /// <param name="transferInstructionSet"></param>
        /// <returns></returns>
        Task<Guid> CreateTransferStateItem(RsaEncryptedRecipientTransferInstructionSet transferInstructionSet);

        /// <summary>
        /// Gets a state item used to hold incoming transfers
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<IncomingTransferStateItem> GetStateItem(Guid id);

        Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, Stream data);

        Task Quarantine(Guid transferStateItemId, MultipartHostTransferParts part, Stream data);

        Task Reject(Guid transferStateItemId, MultipartHostTransferParts part);

        Task RemoveStateItem(Guid transferStateItemId);
    }
}