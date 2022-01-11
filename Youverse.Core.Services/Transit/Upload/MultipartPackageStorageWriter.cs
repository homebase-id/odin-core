using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class MultipartPackageStorageWriter : IMultipartPackageStorageWriter
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly IAppService _appService;
        private readonly Dictionary<Guid, UploadPackage> _packages;
        private readonly Dictionary<Guid, int> _partCounts;

        private byte[] _initializationVector;

        public MultipartPackageStorageWriter(DotYouContext context, ILogger<IMultipartPackageStorageWriter> logger, IDriveService driveService, IAppService appService)
        {
            _context = context;
            _driveService = driveService;
            _appService = appService;
            _packages = new Dictionary<Guid, UploadPackage>();
            _partCounts = new Dictionary<Guid, int>();
        }

        public async Task<Guid> CreatePackage(Stream data)
        {
            //TODO: need to partially encrypt upload instruction set
            string json = await new StreamReader(data).ReadToEndAsync();
            var instructionSet = JsonConvert.DeserializeObject<UploadInstructionSet>(json);

            if (null == instructionSet?.TransferIv || ByteArrayUtil.EquiByteArrayCompare(instructionSet.TransferIv, Guid.Empty.ToByteArray()))
            {
                throw new UploadException("Invalid or missing instruction set or transfer initialization vector");
            }

            _initializationVector = instructionSet.TransferIv;
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();

            //Use the drive requested, if set
            if (instructionSet.StorageOptions?.DriveId.HasValue ?? false)
            {
                driveId = instructionSet.StorageOptions.DriveId.Value;
            }

            if (driveId == Guid.Empty)
            {
                throw new UploadException("Missing or invalid driveId");
            }

            if (instructionSet.TransitOptions?.Recipients?.Contains(_context.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer to yourself; what's the point?");
            }

            var pkgId = Guid.NewGuid();
            var file = instructionSet.StorageOptions?.GetFile() ?? _driveService.CreateFileId(driveId);
            var package = new UploadPackage(file, instructionSet!);
            _packages.Add(pkgId, package);
            _partCounts.Add(pkgId, 1);

            return pkgId;
        }

        public async Task<bool> AddPart(Guid pkgId, string name, Stream data)
        {
            if (!_packages.TryGetValue(pkgId, out var pkg))
            {
                throw new UploadException("Invalid package Id");
            }

            if (!Enum.TryParse<MultipartSectionNames>(name, true, out var part))
            {
                throw new UploadException("Invalid part name specified");
            }

            if (part == MultipartSectionNames.Metadata)
            {
                var descriptor = await this.Decrypt<UploadFileDescriptor>(data);
                var transferEncryptedKeyHeader = descriptor.EncryptedKeyHeader;

                if (null == transferEncryptedKeyHeader)
                {
                    throw new UploadException("Invalid transfer key header");
                }

                await _appService.WriteTransferKeyHeader(pkg.File, transferEncryptedKeyHeader, StorageDisposition.Temporary);

                var metadata = new FileMetadata(pkg.File)
                {
                    ContentType = descriptor.FileMetadata.ContentType,
                    
                    AppData = new AppFileMetaData()
                    {
                        CategoryId = descriptor.FileMetadata.AppData.CategoryId,
                        JsonContent = descriptor.FileMetadata.AppData.JsonContent,
                        ContentIsComplete = descriptor.FileMetadata.AppData.ContentIsComplete
                    }
                };

                //TODO: need to combine with write transfer keyheader and put on _appService
                await _driveService.WriteMetaData(pkg.File, metadata, StorageDisposition.Temporary);

                _partCounts[pkgId]++;
            }
            else if (part == MultipartSectionNames.Payload)
            {
                await _driveService.WritePayload(pkg.File, data, StorageDisposition.Temporary);
                _partCounts[pkgId]++;
            }
            else if (part == MultipartSectionNames.Instructions)
            {
                throw new UploadException("MultipartSectionNames.Instructions must be used by CreatePackage");
            }
            else
            {
                //Not sure how we got here but just in case
                throw new InvalidDataException($"Part name [{name}] not recognized");
            }

            return _partCounts[pkgId] == pkg.ExpectedPartsCount;
        }

        public async Task<UploadPackage> GetPackage(Guid packageId)
        {
            if (_packages.TryGetValue(packageId, out var package))
            {
                return package;
            }

            return null;
        }

        private async Task<T> Decrypt<T>(Stream data)
        {
            if (null == _initializationVector)
            {
                throw new UploadException($"The part named [{Enum.GetName(MultipartSectionNames.Instructions)}] must be provided first");
            }

            byte[] encryptedBytes;
            await using (var ms = new MemoryStream())
            {
                await data.CopyToAsync(ms);
                encryptedBytes = ms.ToArray();
            }

            var json = AesCbc.DecryptStringFromBytes_Aes(encryptedBytes, this._context.AppContext.GetClientSharedSecret().GetKey(), this._initializationVector);
            var t = JsonConvert.DeserializeObject<T>(json);
            return t;
        }
    }
}