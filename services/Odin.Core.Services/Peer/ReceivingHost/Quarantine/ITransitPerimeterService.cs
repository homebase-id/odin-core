using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.ReceivingHost.Quarantine
{
    /// <summary>
    /// Handles incoming payloads at the perimeter of the DI host.  
    /// </summary>
    public interface ITransitPerimeterService
    {
        /// <summary>
        /// Prepares a holder for an incoming file and returns the Id.  You should use this Id on calls to <see cref="ApplyFirstStageFiltering"/>
        /// </summary>
        /// <param name="transferInstructionSet"></param>
        /// <returns></returns>
        Task<Guid> InitializeIncomingTransfer(EncryptedRecipientTransferInstructionSet transferInstructionSet);

        /// <summary>
        /// Filters, Triages, and distributes the incoming payload the right handler
        /// </summary>
        /// <returns></returns>
        Task<AddPartResponse> ApplyFirstStageFiltering(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data);

        /// <summary>
        /// Indicates if the file has all required parts and all parts are valid
        /// </summary>
        /// <returns></returns>
        Task<bool> IsFileValid(Guid transferStateItemId);

        /// <summary>
        /// Finalizes the transfer after having applied the full set of filters to all parts of the incoming file.
        /// </summary>
        Task<HostTransitResponse> FinalizeTransfer(Guid transferStateItemId, FileMetadata fileMetadata);

        /// <summary>
        /// Deletes a file that was linked with a GlobalTransitId
        /// </summary>
        Task<HostTransitResponse> AcceptDeleteLinkedFileRequest(TargetDrive targetDrive, Guid globalTransitId, FileSystemType transitRequestFileSystemType);

        Task<QueryBatchResult> QueryBatch(FileQueryParams qp, QueryBatchResultOptions options);

        Task<SharedSecretEncryptedFileHeader> GetFileHeader(TargetDrive targetDrive, Guid fileId);

        Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, Stream stream)> GetPayloadStream(TargetDrive targetDrive,
            Guid fileId, string key, FileChunk chunk);

        Task<(string encryptedKeyHeader64, bool payloadIsEncrypted, string decryptedContentType, Stream stream)> GetThumbnail(TargetDrive targetDrive,
            Guid fileId, int height, int width);

        Task<IEnumerable<PerimeterDriveData>> GetDrives(Guid driveType);
    }
}