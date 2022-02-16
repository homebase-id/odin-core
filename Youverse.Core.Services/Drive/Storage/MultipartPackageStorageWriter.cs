using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private readonly Dictionary<Guid, UploadPackage> _packages;

        public MultipartPackageStorageWriter(DotYouContext context, ILogger<IMultipartPackageStorageWriter> logger, IDriveService driveService)
        {
            _context = context;
            _driveService = driveService;
            _packages = new Dictionary<Guid, UploadPackage>();
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

            if (instructionSet.TransitOptions?.Recipients?.Contains(_context.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer to yourself; what's the point?");
            }

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

            DriveFileId file;
            if (instructionSet.StorageOptions?.OverwriteFileId.HasValue ?? false)
            {
                //file to overwrite
                file = new DriveFileId()
                {
                    DriveId = driveId,
                    FileId = instructionSet.StorageOptions.OverwriteFileId.GetValueOrDefault()
                };
            }
            else
            {
                //get a new fileid
                file = _driveService.CreateFileId(driveId);
            }

            var pkgId = Guid.NewGuid();
            var package = new UploadPackage(file, instructionSet!);
            _packages.Add(pkgId, package);

            return pkgId;
        }

        public async Task AddMetadata(Guid packageId, Stream data)
        {
            if (!_packages.TryGetValue(packageId, out var pkg))
            {
                throw new UploadException("Invalid package Id");
            }

            await _driveService.WriteTempStream(pkg.File, MultipartUploadParts.Metadata.ToString(), data);
        }

        public async Task AddPayload(Guid packageId, Stream data)
        {
            if (!_packages.TryGetValue(packageId, out var pkg))
            {
                throw new UploadException("Invalid package Id");
            }

            await _driveService.WriteTempStream(pkg.File, MultipartUploadParts.Payload.ToString(), data);
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