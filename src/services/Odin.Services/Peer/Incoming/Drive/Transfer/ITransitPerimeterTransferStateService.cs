using System;
using System.IO;
using System.Threading.Tasks;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
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

        Task RemoveStateItem(Guid transferStateItemId);
    }
}