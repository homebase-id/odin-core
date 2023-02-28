using System;
using Dawn;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Incoming
{
    public class IncomingTransferStateItem
    {
        public IncomingTransferStateItem(GuidId id, InternalDriveFileId tempFile, FileSystemType transferTransferFileSystemType)
        {
            Guard.Argument(id, nameof(id)).NotNull().Require(x => GuidId.IsValid(x));
            Guard.Argument(tempFile, nameof(tempFile)).Require(tempFile.IsValid());

            this.Id = id;
            this.TempFile = tempFile;
            this.TransferFileSystemType = transferTransferFileSystemType;

            this.HeaderState = new();
            this.MetadataState = new();
            this.PayloadState = new();
        }

        public FileSystemType TransferFileSystemType { get; init; }

        public GuidId Id { get; init; }

        /// <summary>
        /// The CRC of the Transit public key used when receiving this transfer
        /// </summary>
        public uint PublicKeyCrc { get; set; }

        public InternalDriveFileId TempFile { get; set; }

        public PartState HeaderState { get; set; }
        public PartState MetadataState { get; set; }
        public PartState PayloadState { get; set; }

        public void SetFilterState(MultipartHostTransferParts part, FilterAction state)
        {
            switch (part)
            {
                case MultipartHostTransferParts.TransferKeyHeader:
                    HeaderState.SetFilterState(state);
                    break;

                case MultipartHostTransferParts.Metadata:
                    MetadataState.SetFilterState(state);
                    break;

                case MultipartHostTransferParts.Payload:
                    PayloadState.SetFilterState(state);
                    break;
            }
        }

        /// <summary>
        /// Indicates if the marker has at least one part that's been rejected by a filter
        /// </summary>
        public bool HasAcquiredRejectedPart()
        {
            return HeaderState.IsRejected() || MetadataState.IsRejected() || PayloadState.IsRejected();
        }

        /// <summary>
        /// Indicates if the marker has at least one part that's been quarantined by a filter
        /// </summary>
        public bool HasAcquiredQuarantinedPart()
        {
            return HeaderState.IsQuarantined() || MetadataState.IsQuarantined() || PayloadState.IsQuarantined();
        }

        public bool IsCompleteAndValid()
        {
            return this.HeaderState.IsValid() && MetadataState.IsValid(); //&& PayloadState.IsValid();
        }

        public class PartState
        {
            /// <summary>
            /// Specifies the part has been provided to the perimeter service
            /// </summary>
            public bool IsAcquired { get; set; }

            /// <summary>
            /// Specifies the result of the filter applied to the part
            /// </summary>
            public FilterAction FilterResult { get; set; }

            public bool IsValid()
            {
                return IsAcquired && FilterResult == FilterAction.Accept;
            }

            public bool IsRejected()
            {
                return this.IsAcquired && this.FilterResult == FilterAction.Reject;
            }

            public bool IsQuarantined()
            {
                return this.IsAcquired && this.FilterResult == FilterAction.Quarantine;
            }

            public void SetFilterState(FilterAction state)
            {
                this.FilterResult = state;
                this.IsAcquired = true;
            }
        }
    }
}