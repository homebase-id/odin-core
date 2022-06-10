using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.SystemStorage;
using Youverse.Core.SystemStorage.SqliteKeyValue;
using Youverse.Core.Util;
using KeyValueDatabase = Youverse.Core.SystemStorage.SqliteKeyValue.KeyValueDatabase;

namespace Youverse.Core.Services.Drive
{
    // Note: drive storage using the ThreeKey KeyValueDatabase
    // key1 = drive id
    // key2 = drive type  + drive alias (see TargetDrive.ToKey() method)
    // key3 = type of data identifier (the fact this is a drive; note: we should put datatype on the KV database)

    public class DriveService : IDriveService
    {
        const int MaxPayloadMemorySize = 4 * 1000; //TODO: put in config

        private readonly IDriveAclAuthorizationService _driveAclAuthorizationService;
        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly TenantContext _tenantContext;
        private readonly ConcurrentDictionary<Guid, ILongTermStorageManager> _longTermStorageManagers;
        private readonly ConcurrentDictionary<Guid, ITempStorageManager> _tempStorageManagers;
        private readonly ILoggerFactory _loggerFactory;

        public DriveService(DotYouContextAccessor contextAccessor, ISystemStorage systemStorage, ILoggerFactory loggerFactory, IMediator mediator,
            IDriveAclAuthorizationService driveAclAuthorizationService, TenantContext tenantContext)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;

            _loggerFactory = loggerFactory;
            _mediator = mediator;
            _driveAclAuthorizationService = driveAclAuthorizationService;
            _tenantContext = tenantContext;
            _longTermStorageManagers = new ConcurrentDictionary<Guid, ILongTermStorageManager>();
            _tempStorageManagers = new ConcurrentDictionary<Guid, ITempStorageManager>();

            InitializeStorageDrives().GetAwaiter().GetResult();
        }

        private readonly object _createDriveLock = new object();
        private readonly byte[] _driveDataType = "drive".ToUtf8ByteArray(); //keep it lower case

        public Task<StorageDrive> CreateDrive(string name, TargetDrive targetDrive, string metadata, bool allowAnonymousReads = false)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();

            var mk = _contextAccessor.GetCurrent().Caller.GetMasterKey();

            StorageDrive storageDrive;

            lock (_createDriveLock)
            {
                //driveAlias and type must be unique
                if (null != this.GetDriveIdByAlias(targetDrive, false).GetAwaiter().GetResult())
                {
                    throw new ConfigException("Drive alias and type must be unique");
                }

                var driveKey = new SymmetricKeyEncryptedAes(ref mk);

                var id = Guid.NewGuid();
                var secret = driveKey.DecryptKeyClone(ref mk);

                (byte[] encryptedIdIv, byte[] encryptedIdValue) = AesCbc.Encrypt(id.ToByteArray(), ref secret);

                var sdb = new StorageDriveBase()
                {
                    Id = id,
                    Name = name,
                    Alias = targetDrive.Alias,
                    Type = targetDrive.Type,
                    Metadata = metadata,
                    MasterKeyEncryptedStorageKey = driveKey,
                    EncryptedIdIv = encryptedIdIv,
                    EncryptedIdValue = encryptedIdValue,
                    AllowAnonymousReads = allowAnonymousReads,
                };

                secret.Wipe();

                var json = JsonConvert.SerializeObject(sdb);
                _systemStorage.KeyValueStorage.ThreeKeyStorage.UpsertRow(sdb.Id.ToByteArray(), targetDrive.ToKey(), _driveDataType, json.ToUtf8ByteArray());

                storageDrive = ToStorageDrive(sdb);
                storageDrive.EnsureDirectories();
            }

            return Task.FromResult(storageDrive);
        }

        public async Task<StorageDrive> GetDrive(Guid driveId, bool failIfInvalid = false)
        {
            var bytes = _systemStorage.KeyValueStorage.ThreeKeyStorage.Get(driveId.ToByteArray());
            if (null == bytes)
            {
                if (failIfInvalid)
                {
                    throw new InvalidDriveException(driveId);
                }

                return null;
            }

            var sdb = ToStorageDriveBase(bytes);
            var drive = ToStorageDrive(sdb);
            return drive;
        }

        public async Task<Guid?> GetDriveIdByAlias(TargetDrive targetDrive, bool failIfInvalid = false)
        {
            var list = _systemStorage.KeyValueStorage.ThreeKeyStorage.GetByKeyTwo(targetDrive.ToKey());
            if (null == list || !list.Any())
            {
                if (failIfInvalid)
                {
                    throw new InvalidDriveException(targetDrive.Alias);
                }

                return null;
            }

            var sdb = ToStorageDriveBase(list.Single());
            var drive = ToStorageDrive(sdb);
            return drive.Id;
        }

        public async Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions)
        {
            return await this.GetDrivesInternal(true, pageOptions);
        }

        public async Task<PagedResult<StorageDrive>> GetDrives(Guid type, PageOptions pageOptions)
        {
            Func<StorageDrive, bool> predicate = drive => drive.Type == type;

            if (_contextAccessor.GetCurrent().Caller.IsAnonymous)
            {
                predicate = drive => drive.Type == type && drive.AllowAnonymousReads == true;
            }

            var page = await this.GetDrivesInternal(false, pageOptions);

            var storageDrives = page.Results.Where(predicate).ToList();
            var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
            return results;
        }

        public async Task<PagedResult<StorageDrive>> GetAnonymousDrives(PageOptions pageOptions)
        {
            Func<StorageDrive, bool> predicate = drive => drive.AllowAnonymousReads;
            var page = await this.GetDrivesInternal(false, pageOptions);

            var storageDrives = page.Results.Where(predicate).ToList();
            var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
            return results;
        }

        public InternalDriveFileId CreateInternalFileId(Guid driveId)
        {
            //TODO: need a permission specifically for writing to the temp drive
            //_contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);

            var df = new InternalDriveFileId()
            {
                FileId = GetLongTermStorageManager(driveId).CreateFileId(),
                DriveId = driveId,
            };

            return df;
        }

        public async Task WriteFileHeader(InternalDriveFileId file, ServerFileHeader header)
        {
            Guard.Argument(header, nameof(header)).NotNull();
            Guard.Argument(header, nameof(header)).Require(x => x.IsValid());

            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var metadata = header.FileMetadata;

            //TODO: need to encrypt the metadata parts
            metadata.File = file; //TBH it's strange having this but we need the metadata to have the file and drive embedded

            if (this.FileExists(file))
            {
                var existingHeader = await this.GetServerFileHeader(file);
                metadata.Updated = DateTimeExtensions.UnixTimeMilliseconds();
                metadata.Created = existingHeader.FileMetadata.Created;
            }
            else
            {
                metadata.Created = DateTimeExtensions.UnixTimeMilliseconds();
            }

            var json = JsonConvert.SerializeObject(header);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var result = GetLongTermStorageManager(file.DriveId).WritePartStream(file.FileId, FilePart.Header, stream);

            OnLongTermFileChanged(file, header);
        }

        public Task WritePartStream(InternalDriveFileId file, FilePart filePart, Stream stream)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var task = GetLongTermStorageManager(file.DriveId).WritePartStream(file.FileId, filePart, stream);
            return task;
        }

        public async Task<T> GetDeserializedStream<T>(InternalDriveFileId file, string extension, StorageDisposition disposition = StorageDisposition.LongTerm)
        {
            this.AssertCanReadDrive(file.DriveId);

            if (disposition == StorageDisposition.LongTerm)
            {
                throw new NotImplementedException("Not supported for long term storage");
            }

            var stream = await this.GetTempStream(file, extension);
            string json = await new StreamReader(stream).ReadToEndAsync();
            var o = JsonConvert.DeserializeObject<T>(json);
            return o;
        }

        private void AssertCanReadDrive(Guid driveId)
        {
            var drive = this.GetDrive(driveId, true).GetAwaiter().GetResult();
            if (!drive.AllowAnonymousReads)
            {
                _contextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);
            }
        }

        public Task WriteTempStream(InternalDriveFileId file, string extension, Stream stream)
        {
            //TODO: need a permission specificallyt for writing to the t4mep drive
            //_contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).WriteStream(file.FileId, extension, stream);
        }

        public Task<Stream> GetTempStream(InternalDriveFileId file, string extension)
        {
            this.AssertCanReadDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).GetStream(file.FileId, extension);
        }

        public Task DeleteTempFile(InternalDriveFileId file, string extension)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).Delete(file.FileId, extension);
        }

        public Task DeleteTempFiles(InternalDriveFileId file)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            return GetTempStorageManager(file.DriveId).Delete(file.FileId);
        }

        public Task<IEnumerable<ServerFileHeader>> GetMetadataFiles(Guid driveId, PageOptions pageOptions)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanReadDrive(driveId);

            return GetLongTermStorageManager(driveId).GetServerFileHeaders(pageOptions);
        }

        private async Task<EncryptedKeyHeader> EncryptKeyHeader(InternalDriveFileId file, KeyHeader keyHeader)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var manager = GetLongTermStorageManager(file.DriveId);
            var drive = manager.Drive;
            var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(file.DriveId);

            //this.AssertKeyMatch(storageKey)
            var decryptedDriveId = AesCbc.Decrypt(drive.EncryptedIdValue, ref storageKey, drive.EncryptedIdIv);
            if (!ByteArrayUtil.EquiByteArrayCompare(decryptedDriveId, drive.Id.ToByteArray()))
            {
                throw new YouverseSecurityException("Invalid key storage attempted to encrypt data");
            }

            var encryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, keyHeader.Iv, ref storageKey);
            return encryptedKeyHeader;
        }

        public async Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);

            var header = await GetLongTermStorageManager(file.DriveId).GetServerFileHeader(file.FileId);

            // var acl = await GetAcl(file);
            await _driveAclAuthorizationService.AssertCallerHasPermission(header.ServerMetadata.AccessControlList);

            //return the acl as a separate value for any internal code that needs to use it
            return header;
        }

        public async Task<Stream> GetPayloadStream(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);

            var stream = await GetLongTermStorageManager(file.DriveId).GetFilePartStream(file.FileId, FilePart.Payload);
            return stream;
        }

        public async Task<(bool tooLarge, long size, byte[] bytes)> GetPayloadBytes(InternalDriveFileId file)
        {
            var size = await this.GetPayloadSize(file);
            if (size > MaxPayloadMemorySize)
            {
                return (true, size, new byte[] { });
            }

            var stream = await this.GetPayloadStream(file);
            var bytes = stream.ToByteArray();
            stream.Close();
            return (false, size, bytes);
        }

        public Task<long> GetPayloadSize(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);

            return GetLongTermStorageManager(file.DriveId).GetPayloadFileSize(file.FileId);
        }

        public void AssertFileIsValid(InternalDriveFileId file)
        {
            GetLongTermStorageManager(file.DriveId).AssertFileIsValid(file.FileId);
        }

        public bool FileExists(InternalDriveFileId file)
        {
            this.AssertCanReadDrive(file.DriveId);
            return GetLongTermStorageManager(file.DriveId).FileExists(file.FileId);
        }

        public Task DeleteLongTermFile(InternalDriveFileId file)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            var result = GetLongTermStorageManager(file.DriveId).Delete(file.FileId);

            var notification = new DriveFileDeletedNotification()
            {
                File = file
            };

            _mediator.Publish(notification);

            return result;
        }

        public async Task CommitTempFileToLongTerm(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata, string payloadExtension)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(file.DriveId);

            //TODO: this method is so hacky 🤢
            metadata.File = file;

            string sourceFile = await GetTempStorageManager(file.DriveId).GetPath(file.FileId, payloadExtension);
            await GetLongTermStorageManager(file.DriveId).MoveToLongTerm(file.FileId, sourceFile, FilePart.Payload);

            //TODO: calculate payload checksum, put on file metadata
            var serverHeader = new ServerFileHeader()
            {
                EncryptedKeyHeader = await this.EncryptKeyHeader(file, keyHeader),
                FileMetadata = metadata,
                ServerMetadata = serverMetadata
            };

            //Note: calling write metadata last since it will call OnLongTermFileChanged to ensure it is indexed
            await this.WriteFileHeader(file, serverHeader);
        }

        private async Task<PagedResult<StorageDrive>> GetDrivesInternal(bool enforceSecurity, PageOptions pageOptions)
        {
            var driveByteList = _systemStorage.KeyValueStorage.ThreeKeyStorage.GetByKeyThree(_driveDataType);

            var predicate = new Func<StorageDriveBase, bool>(drive => true);
            if (enforceSecurity)
            {
                if (_contextAccessor.GetCurrent().Caller.IsAnonymous)
                {
                    predicate = drive => drive.AllowAnonymousReads == true;
                }
            }

            var storageDrives = driveByteList.Select(ToStorageDriveBase);
            var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives.Where(predicate).Select(ToStorageDrive).ToList());
            return results;
        }


        private StorageDriveBase ToStorageDriveBase(byte[] bytes)
        {
            return JsonConvert.DeserializeObject<StorageDriveBase>(bytes.ToStringFromUtf8Bytes());
        }

        private void OnLongTermFileChanged(InternalDriveFileId file, ServerFileHeader header)
        {
            var notification = new DriveFileChangedNotification()
            {
                File = file,
                FileHeader = header
            };

            _mediator.Publish(notification);
        }

        private StorageDrive ToStorageDrive(StorageDriveBase sdb)
        {
            //TODO: this should probably go in config
            const string driveFolder = "drives";
            return new StorageDrive(
                Path.Combine(_tenantContext.StorageConfig.DataStoragePath, driveFolder),
                Path.Combine(_tenantContext.StorageConfig.TempStoragePath, driveFolder), sdb);
        }

        private async Task InitializeStorageDrives()
        {
            var drives = await this.GetDrivesInternal(false, PageOptions.All);
            foreach (var drive in drives.Results)
            {
                LoadLongTermStorage(drive, out var _);
            }
        }

        private ILongTermStorageManager GetLongTermStorageManager(Guid driveId)
        {
            if (_longTermStorageManagers.TryGetValue(driveId, out var manager))
            {
                return manager;
            }

            var sd = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var success = LoadLongTermStorage(sd, out manager);
            if (!success)
            {
                throw new StorageException($"Could not load long term storage for drive {driveId}");
            }

            return manager;
        }

        private ITempStorageManager GetTempStorageManager(Guid driveId)
        {
            if (_tempStorageManagers.TryGetValue(driveId, out var manager))
            {
                return manager;
            }

            var sd = this.GetDrive(driveId, failIfInvalid: true).GetAwaiter().GetResult();
            var success = LoadTempStorage(sd, out manager);
            if (!success)
            {
                throw new StorageException($"Could not load temporary storage for drive {driveId}");
            }

            return manager;
        }

        private bool LoadLongTermStorage(StorageDrive drive, out ILongTermStorageManager manager)
        {
            var logger = _loggerFactory.CreateLogger<ILongTermStorageManager>();
            manager = new FileBasedLongTermStorageManager(drive, logger);
            return _longTermStorageManagers.TryAdd(drive.Id, manager);
        }

        private bool LoadTempStorage(StorageDrive drive, out ITempStorageManager manager)
        {
            var logger = _loggerFactory.CreateLogger<ITempStorageManager>();
            manager = new FileBasedTempStorageManager(drive, logger);
            return _tempStorageManagers.TryAdd(drive.Id, manager);
        }
    }
}