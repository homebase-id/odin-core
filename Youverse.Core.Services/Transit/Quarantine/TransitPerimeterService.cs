using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
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
        private readonly IDictionary<Guid, FileTracker> _fileTrackers;
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
            _fileTrackers = new Dictionary<Guid, FileTracker>();
        }

        public async Task<Guid> CreateFileTracker()
        {
            var id = await this.AuditWriter.CreateAuditTrackerId();
            _fileTrackers.Add(id, new FileTracker(id));
            return id;
        }

        public async Task<AddPartResponse> ApplyFirstStageFilterToPart(Guid fileId, FilePart part, Stream data)
        {
            var tracker = GetTrackerOrFail(fileId);

            if (tracker.HasAcquiredRejectedPart())
            {
                throw new InvalidDataException("Corresponding part has been rejected");
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
                _transitService.AcceptTransfer(tracker.Id, tracker.File.GetValueOrDefault());

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

            throw new Exception("Unhandled error");
        }

        public async Task<TransitPublicKey> GetTransitPublicKey()
        {
            var rsaKeyList = await this.GetRsaKeyList();
            var key = RsaKeyListManagement.GetCurrentKey(ref rsaKeyList, out var keyListWasUpdated);

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

            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(MAX_KEYS);
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
            throw new NotImplementedException("TODO wip");
            
            var keys = await GetRsaKeyList();
            var pk = RsaKeyListManagement.FindKey(keys, header.PublicKeyCrc);

            if (pk == null)
            {
                throw new InvalidDataException("Invalid public key");
            }

            var decryptedBytes = pk.Decrypt(header.EncryptedAesKey).ToSensitiveByteArray();
            var keyHeader = KeyHeader.FromCombinedBytes(decryptedBytes.GetKey(), 16,16);
            decryptedBytes.Wipe();
        }

        private FileTracker GetTrackerOrFail(Guid trackerId)
        {
            if (!_fileTrackers.TryGetValue(trackerId, out var marker))
            {
                throw new InvalidDataException("Invalid tracker Id");
            }

            return marker;
        }

        private AddPartResponse RejectPart(FileTracker tracker, FilePart part, Stream data)
        {
            //remove all other file parts
            _fileDrive.Delete(tracker.File.GetValueOrDefault(), StorageDisposition.Temporary);

            this.AuditWriter.WriteEvent(tracker.Id, TransitAuditEvent.Rejected);
            //do nothing with the stream since it's bad
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Reject
            };
        }

        private async Task<AddPartResponse> QuarantinePart(FileTracker tracker, FilePart part, Stream data)
        {
            //TODO: move all other file parts to quarantine.

            await _quarantineService.QuarantinePart(tracker.Id, part, data);
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Quarantine,
            };
        }

        private async Task<AddPartResponse> AcceptPart(FileTracker tracker, FilePart part, Stream data)
        {
            data.Position = 0;
            tracker.SetAccepted(part);
            
            //TODO: validate 
            if (tracker.File == null)
            {
                tracker.SetStorageInfo(_fileDrive.CreateFileId(_context.AppContext.DriveId.GetValueOrDefault()));
            }
            
            await _fileDrive.WritePartStream(tracker.File.GetValueOrDefault(), part, data, StorageDisposition.Temporary);

            //triage, decrypt, route the payload
            var result = new AddPartResponse()
            {
                FilterAction = FilterAction.Accept
            };

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

        private class FileTracker
        {
            public FileTracker(Guid id)
            {
                Guard.Argument(id, nameof(id)).NotEqual(Guid.Empty);
                this.Id = id;
            }

            /// <summary>
            /// The id of this tracker
            /// </summary>
            public Guid Id { get; init; }

            public DriveFileId? File { get; set; }

            public PartState HeaderState;
            public PartState MetadataState;
            public PartState PayloadState;

            public void SetAccepted(FilePart part)
            {
                switch (part)
                {
                    case FilePart.Header:
                        HeaderState.SetValid();
                        break;

                    case FilePart.Metadata:
                        MetadataState.SetValid();
                        break;

                    case FilePart.Payload:
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

            /// <summary>
            /// Sets the FileId to be used when storing the file
            /// </summary>
            public void SetStorageInfo(DriveFileId file)
            {
                Guard.Argument(file, nameof(file)).Require(file.IsValid());

                if (File.HasValue)
                {
                    throw new Exception("Drive and/or FileId is already set");
                }

                File = file;
            }
        }
    }
}