using Dawn;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming.Drive;

namespace Odin.Core.Services.Peer.Incoming.Storage
{
    public class IncomingTransferStateItem
    {
        public IncomingTransferStateItem(GuidId id, InternalDriveFileId tempFile, EncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            Guard.Argument(id, nameof(id)).NotNull().Require(x => GuidId.IsValid(x));
            Guard.Argument(tempFile, nameof(tempFile)).Require(tempFile.IsValid());

            this.Id = id;
            this.TempFile = tempFile;
            this.TransferInstructionSet = transferInstructionSet;
            this.HeaderState = new();
            this.MetadataState = new();
            this.PayloadState = new();
        }

        public EncryptedRecipientTransferInstructionSet TransferInstructionSet { get; init; }

        public GuidId Id { get; init; }

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