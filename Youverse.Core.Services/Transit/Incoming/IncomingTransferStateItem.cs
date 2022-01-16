using System;
using Dawn;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Incoming
{
    public class IncomingTransferStateItem
    {
        public IncomingTransferStateItem(Guid id, DriveFileId tempFile)
        {
            Guard.Argument(id, nameof(id)).NotEqual(Guid.Empty);
            Guard.Argument(tempFile, nameof(tempFile)).Require(tempFile.IsValid());

            this.Id = id;
            this.TempFile = tempFile;
        }

        public Guid Id { get; init; }
        
        /// <summary>
        /// The CRC of the Transit public key used when receiving this transfer
        /// </summary>
        public uint PublicKeyCrc { get; set; }
        
        public DriveFileId TempFile { get; set; }

        public PartState HeaderState { get; set; }
        public PartState MetadataState{ get; set; }
        public PartState PayloadState{ get; set; }
        
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
            return this.HeaderState.IsValid() && MetadataState.IsValid() && PayloadState.IsValid();
        }
        
        public struct PartState
        {
            /// <summary>
            /// Specifies the part has been provided to the perimeter service
            /// </summary>
            public bool IsAcquired;

            /// <summary>
            /// Specifies the result of the filter applied to the part
            /// </summary>
            public FilterAction FilterResult;

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