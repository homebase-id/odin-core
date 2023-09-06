using System;
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost.Incoming;

namespace Odin.Core.Services.Peer.ReceivingHost.Quarantine
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
        Task<Guid> CreateTransferStateItem(EncryptedRecipientTransferInstructionSet transferInstructionSet);

        /// <summary>
        /// Gets a state item used to hold incoming transfers
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<IncomingTransferStateItem> GetStateItem(Guid id);

        Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data);

        Task Quarantine(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data);

        Task Reject(Guid transferStateItemId, MultipartHostTransferParts part);

        Task RemoveStateItem(Guid transferStateItemId);
    }
}