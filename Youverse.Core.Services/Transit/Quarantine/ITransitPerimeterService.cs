using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Handles incoming payloads at the perimeter of the DI host.  
    /// </summary>
    public interface ITransitPerimeterService
    {
        /// <summary>
        /// Prepares a holder for an incoming file and returns the Id.  You should use this Id on calls to <see cref="ApplyFirstStageFiltering"/>
        /// </summary>
        /// <param name="rsaKeyHeader"></param>
        /// <returns></returns>
        Task<Guid> InitializeIncomingTransfer(RsaEncryptedRecipientTransferKeyHeader rsaKeyHeader);

        /// <summary>
        /// Filters, Triages, and distributes the incoming payload the right handler
        /// </summary>
        /// <returns></returns>
        Task<AddPartResponse> ApplyFirstStageFiltering(Guid transferStateItemId, MultipartHostTransferParts part, Stream data);

        /// <summary>
        /// Indicates if the file has all required parts and all parts are valid
        /// </summary>
        /// <returns></returns>
        Task<bool> IsFileValid(Guid transferStateItemId);

        /// <summary>
        /// Finalizes the transfer after having applied the full set of filters to all parts of the incoming file.
        /// </summary>
        Task<FilterAction> FinalizeTransfer(Guid transferStateItemId);


        /// <summary>
        /// Returns the public key to be used for encrypting the <see cref="EncryptedKeyHeader"/> during data transfer
        /// </summary>
        /// <returns></returns>
        Task<TransitPublicKey> GetTransitPublicKey();
    }
}