using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Upload
{
    public class MultipartPackageStorageWriter : IMultipartPackageStorageWriter
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly Dictionary<Guid, UploadPackage> _packages;
        private readonly Dictionary<Guid, int> _partCounts;

        private byte[] _initializationVector;

        public MultipartPackageStorageWriter(DotYouContext context, ILogger<IMultipartPackageStorageWriter> logger, IDriveService driveService)
        {
            _context = context;
            _driveService = driveService;
            _packages = new Dictionary<Guid, UploadPackage>();
            _partCounts = new Dictionary<Guid, int>();
        }

        //TODO: expect parts is a hack until we redsign the upload spec
        public Task<Guid> CreatePackage(Guid driveId, int expectedPartCount = 4)
        {
            var pkgId = Guid.NewGuid();
            _packages.Add(pkgId, new UploadPackage(_driveService.CreateFileId(driveId), expectedPartCount));
            _partCounts.Add(pkgId, 0);
            return Task.FromResult(pkgId);
        }

        public async Task<bool> AddPart(Guid pkgId, string name, Stream data)
        {
            if (!_packages.TryGetValue(pkgId, out var pkg))
            {
                throw new Exception("Invalid package ID");
            }

            if (string.Equals(name, MultipartSectionNames.TransferEncryptedKeyHeader, StringComparison.InvariantCultureIgnoreCase))
            {
                string json = await new StreamReader(data).ReadToEndAsync();
                var transferEncryptedKeyHeader = JsonConvert.DeserializeObject<EncryptedKeyHeader>(json);

                if (null == transferEncryptedKeyHeader)
                {
                    throw new InvalidDataException("Invalid transfer key header");
                }

                await _driveService.WriteTransferKeyHeader(pkg.File, transferEncryptedKeyHeader, StorageDisposition.Temporary);
                _initializationVector = transferEncryptedKeyHeader.Iv; //saved for decrypting recipients

                _partCounts[pkgId]++;
            }
            else if (string.Equals(name, MultipartSectionNames.Recipients, StringComparison.InvariantCultureIgnoreCase))
            {
                var list = await DecryptDeserializeFromAppSharedSecret<RecipientList>(data);
                if (list?.Recipients.Count <= 0)
                {
                    throw new Exception("No recipients specified");
                }

                pkg.RecipientList = list;
                _partCounts[pkgId]++;
            }
            else
            {
                if (!Enum.TryParse<FilePart>(name, true, out var filePart))
                {
                    throw new InvalidDataException($"Part name [{name}] not recognized");
                }

                if (filePart == FilePart.Header)
                {
                    throw new InvalidDataException($"This header cannot be uploaded from client.  Use {MultipartSectionNames.TransferEncryptedKeyHeader} instead.");
                }

                if (filePart == FilePart.Metadata)
                {
                    var metadata = await DecryptDeserializeFromAppSharedSecret<FileMetaData>(data);
                    await _driveService.WriteMetaData(pkg.File, metadata, StorageDisposition.Temporary);
                    _partCounts[pkgId]++;
                }
                else if (filePart == FilePart.Payload)
                {
                    await _driveService.WritePayload(pkg.File, data, StorageDisposition.Temporary);
                    _partCounts[pkgId]++;
                }
                else
                {
                    //Not sure how we got here but just in case
                    throw new InvalidDataException($"Part name [{name}] not recognized");
                }
            }

            return _partCounts[pkgId] == pkg.ExpectedPartsCount;
        }

        private async Task<T> DecryptDeserializeFromAppSharedSecret<T>(Stream data)
        {
            if (null == _initializationVector)
            {
                throw new InvalidDataException($"The part named [{MultipartSectionNames.TransferEncryptedKeyHeader}] must be provided first");
            }

            byte[] encryptedBytes;
            await using (var ms = new MemoryStream())
            {
                await data.CopyToAsync(ms);
                encryptedBytes = ms.ToArray();
            }

            var json = AesCbc.DecryptStringFromBytes_Aes(encryptedBytes, this._context.AppContext.GetDeviceSharedSecret().GetKey(), this._initializationVector);
            var t = JsonConvert.DeserializeObject<T>(json);
            return t;
        }

        public async Task<UploadPackage> GetPackage(Guid packageId)
        {
            if (_packages.TryGetValue(packageId, out var package))
            {
                return package;
            }

            return null;
        }
    }
}