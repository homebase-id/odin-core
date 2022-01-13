using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterService : TransitServiceBase<ITransitPerimeterService>, ITransitPerimeterService
    {
        private readonly Guid RSA_KEY_STORAGE_ID = Guid.Parse("FFFFFFCF-0f85-DDDD-a7eb-e8e0b06c2555");
        private readonly string RSA_KEY_STORAGE = "transitrks";
        private readonly DotYouContext _context;
        private readonly ITransitService _transitService;
        private readonly IDriveService _fileDrive;
        private readonly IDictionary<Guid, IncomingFileTracker> _fileTrackers;
        private readonly ITransitQuarantineService _quarantineService;
        private readonly ISystemStorage _systemStorage;

        public TransitPerimeterService(
            DotYouContext context,
            ILogger<ITransitPerimeterService> logger,
            ITransitAuditWriterService auditWriter,
            ITransitService transitService,
            ITransitQuarantineService quarantineService,
            IDriveService fileDrive, ISystemStorage systemStorage) : base(auditWriter)
        {
            _context = context;
            _transitService = transitService;
            _quarantineService = quarantineService;
            _fileDrive = fileDrive;
            _systemStorage = systemStorage;
            _fileTrackers = new Dictionary<Guid, IncomingFileTracker>();
        }

        public async Task<Guid> CreateFileTracker(EncryptedRecipientTransferKeyHeader transferKeyHeader)
        {
            Guard.Argument(transferKeyHeader, nameof(transferKeyHeader)).NotNull();
            Guard.Argument(transferKeyHeader.PublicKeyCrc, nameof(transferKeyHeader.PublicKeyCrc)).NotEqual<uint>(0);
            Guard.Argument(transferKeyHeader.EncryptedAesKey.Length, nameof(transferKeyHeader.EncryptedAesKey.Length)).NotEqual(0);

            var file = _fileDrive.CreateFileId(_context.TransitContext.DriveId);
            var id = await this.AuditWriter.CreateAuditTrackerId();

            var tracker = new IncomingFileTracker(
                id: id,
                tempFile: file,
                publicKeyCrc: transferKeyHeader.PublicKeyCrc);

            //Note: we're not currently applying filters to the transfer key header since
            //we generate it.  It is feasible to, at a future point, apply filters by
            //encryption version or strength
            //therefore - just mark it good to go
            tracker.SetAccepted(MultipartHostTransferParts.TransferKeyHeader);

            _fileTrackers.Add(id, tracker);

            return id;
        }

        public async Task<AddPartResponse> ApplyFirstStageFilter(Guid fileId, MultipartHostTransferParts part, Stream data)
        {
            var tracker = GetTrackerOrFail(fileId);

            if (tracker.HasAcquiredRejectedPart())
            {
                throw new HostToHostTransferException("Corresponding part has been rejected");
            }

            if (tracker.HasAcquiredQuarantinedPart())
            {
                //quarantine the rest
                return await QuarantinePart(tracker, part, data);
            }

            var filterResponse = await _quarantineService.ApplyFirstStageFilters(tracker.Id, part, data);

            switch (filterResponse.Code)
            {
                case FinalFilterAction.Accepted:
                    return await AcceptPart(tracker, part, data);

                case FinalFilterAction.QuarantinedPayload:
                case FinalFilterAction.QuarantinedSenderNotConnected:
                    return await QuarantinePart(tracker, part, data);

                case FinalFilterAction.Rejected:
                default:
                    return RejectPart(tracker, part, data);
            }
        }

        public bool IsFileValid(Guid fileId)
        {
            return _fileTrackers[fileId].IsCompleteAndValid();
        }

        public Task<CollectiveFilterResult> FinalizeTransfer(Guid trackerId)
        {
            var tracker = GetTrackerOrFail(trackerId);

            if (tracker.IsCompleteAndValid())
            {
                _transitService.AcceptTransfer(tracker.Id, tracker.TempFile, tracker.PublicKeyCrc);

                var result = new CollectiveFilterResult()
                {
                    Code = FinalFilterAction.Accepted,
                    Message = ""
                };

                return Task.FromResult(result);
            }

            if (tracker.HasAcquiredQuarantinedPart())
            {
                //TODO: how do i know which filter quarantined it??

                //_quarantineService.QuarantinePart(trackerId);

                var result = new CollectiveFilterResult()
                {
                    Code = FinalFilterAction.QuarantinedPayload,
                    Message = ""
                };

                return Task.FromResult(result);
            }

            if (tracker.HasAcquiredRejectedPart())
            {
                var result = new CollectiveFilterResult()
                {
                    Code = FinalFilterAction.Rejected,
                    Message = ""
                };

                AuditWriter.WriteEvent(trackerId, TransitAuditEvent.Rejected);
                return Task.FromResult(result);
            }

            throw new HostToHostTransferException("Unhandled error");
        }

        public async Task<TransitPublicKey> GetTransitPublicKey()
        {
            var rsaKeyList = await this.GetRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(Guid.Empty.ToByteArray().ToSensitiveByteArray(), ref rsaKeyList, out var keyListWasUpdated); // TODO

            if (keyListWasUpdated)
            {
                _systemStorage.WithTenantSystemStorage<RsaKeyListData>(RSA_KEY_STORAGE, s => s.Save(rsaKeyList));
            }

            return new TransitPublicKey
            {
                PublicKey = key.publicKey,
                Expiration = key.expiration,
                Crc = key.crc32c
            };
        }

        private async Task<RsaKeyListData> GenerateRsaKeyList()
        {
            //HACK: need to refactor this when storage is rebuilt 
            const int MAX_KEYS = 4; //leave this size 

            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(Guid.Empty.ToByteArray().ToSensitiveByteArray(), MAX_KEYS); // TODO
            rsaKeyList.Id = RSA_KEY_STORAGE_ID;

            _systemStorage.WithTenantSystemStorage<RsaKeyListData>(RSA_KEY_STORAGE, s => s.Save(rsaKeyList));
            return rsaKeyList;
        }

        private async Task<RsaKeyListData> GetRsaKeyList()
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaKeyListData>(RSA_KEY_STORAGE, s => s.Get(RSA_KEY_STORAGE_ID));

            if (result == null)
            {
                return await this.GenerateRsaKeyList();
            }

            return result;
        }

        private async void DecryptTransferKeyHeader(EncryptedRecipientTransferKeyHeader header)
        {
            //throw new NotImplementedException("TODO wip");

            var keys = await GetRsaKeyList();
            var pk = RsaKeyListManagement.FindKey(keys, header.PublicKeyCrc);

            if (pk == null)
            {
                throw new InvalidDataException("Invalid public key");
            }
            
            // var decryptedBytes = pk.Decrypt(header.EncryptedAesKey.ToSensitiveByteArray()).ToSensitiveByteArray();
            // var keyHeader = KeyHeader.FromCombinedBytes(decryptedBytes.GetKey(), 16, 16);
            
            var decryptedBytes = pk.Decrypt(Guid.Empty.ToByteArray().ToSensitiveByteArray(), header.EncryptedAesKey).ToSensitiveByteArray(); // TODO
            var keyHeader = KeyHeader.FromCombinedBytes(decryptedBytes.GetKey(), 16,16);

            
            decryptedBytes.Wipe();
        }

        private IncomingFileTracker GetTrackerOrFail(Guid trackerId)
        {
            if (!_fileTrackers.TryGetValue(trackerId, out var marker))
            {
                throw new InvalidDataException("Invalid tracker Id");
            }

            return marker;
        }

        private AddPartResponse RejectPart(IncomingFileTracker tracker, MultipartHostTransferParts part, Stream data)
        {
            //Note: we remove all temp files if a single part is rejected
            _fileDrive.DeleteTempFiles(tracker.TempFile);

            this.AuditWriter.WriteEvent(tracker.Id, TransitAuditEvent.Rejected);
            //do nothing with the stream since it's bad
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Reject
            };
        }

        private async Task<AddPartResponse> QuarantinePart(IncomingFileTracker tracker, MultipartHostTransferParts part, Stream data)
        {
            //TODO: move all other file parts to quarantine.

            await _quarantineService.QuarantinePart(tracker.Id, part, data);
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Quarantine,
            };
        }

        private async Task<AddPartResponse> AcceptPart(IncomingFileTracker tracker, MultipartHostTransferParts part, Stream data)
        {
            await _fileDrive.WriteTempStream(tracker.TempFile, part.ToString(), data);

            //triage, decrypt, route the payload
            var result = new AddPartResponse()
            {
                FilterAction = FilterAction.Accept
            };

            tracker.SetAccepted(part);

            return result;
        }

        private struct PartState
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

            public void SetValid()
            {
                this.FilterResult = FilterAction.Accept;
                this.IsAcquired = true;
            }
        }

        private class IncomingFileTracker
        {
            public IncomingFileTracker(Guid id, UInt32 publicKeyCrc, DriveFileId tempFile)
            {
                Guard.Argument(id, nameof(id)).NotEqual(Guid.Empty);
                Guard.Argument(publicKeyCrc, nameof(publicKeyCrc)).NotEqual<UInt32>(0);
                Guard.Argument(tempFile, nameof(tempFile)).Require(tempFile.IsValid());

                this.Id = id;
                this.PublicKeyCrc = publicKeyCrc;
                this.TempFile = tempFile;
            }

            /// <summary>
            /// The CRC of the Transit public key used when receiving this transfer
            /// </summary>
            public uint PublicKeyCrc { get; set; }

            /// <summary>
            /// The id of this tracker
            /// </summary>
            public Guid Id { get; init; }

            public DriveFileId TempFile { get; set; }

            public PartState HeaderState;
            public PartState MetadataState;
            public PartState PayloadState;

            public void SetAccepted(MultipartHostTransferParts part)
            {
                switch (part)
                {
                    case MultipartHostTransferParts.TransferKeyHeader:
                        HeaderState.SetValid();
                        break;

                    case MultipartHostTransferParts.Metadata:
                        MetadataState.SetValid();
                        break;

                    case MultipartHostTransferParts.Payload:
                        PayloadState.SetValid();
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
        }
    }
}