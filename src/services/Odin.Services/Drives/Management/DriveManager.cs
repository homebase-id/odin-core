using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Mediator;

namespace Odin.Services.Drives.Management;

// Note: drive storage using the ThreeKey KeyValueDatabase
// key1 = drive id
// key2 = drive type  + drive alias (see TargetDrive.ToKey() method)
// key3 = type of data identifier (the fact this is a drive; note: we should put datatype on the KV database)

/// <summary>
/// Manages drive creation, metadata updates, and their overall definitions
/// </summary>
public class DriveManager
{
    private readonly ILogger<DriveManager> _logger;
    private readonly IMediator _mediator;

    private readonly TenantContext _tenantContext;

    private readonly ConcurrentDictionary<Guid, StorageDrive> _driveCache;

    private readonly AsyncLock _createDriveLock = new();
    private readonly byte[] _driveDataType = "drive".ToUtf8ByteArray(); //keep it lower case
    private readonly ThreeKeyValueStorage _driveStorage;

    public DriveManager(ILogger<DriveManager> logger, TenantSystemStorage tenantSystemStorage, IMediator mediator, TenantContext tenantContext)
    {
        _logger = logger;
        _mediator = mediator;
        _tenantContext = tenantContext;
        _driveCache = new ConcurrentDictionary<Guid, StorageDrive>();

        const string driveContextKey = "4cca76c6-3432-4372-bef8-5f05313c0376";
        _driveStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(driveContextKey));
        LoadCache(tenantSystemStorage.IdentityDatabase);
    }

    public async Task<StorageDrive> CreateDrive(CreateDriveRequest request, IOdinContext odinContext, IdentityDatabase db)
    {
        if (string.IsNullOrEmpty(request?.Name))
        {
            throw new OdinClientException("Name cannot be empty");
        }

        if (request.OwnerOnly && request.AllowAnonymousReads)
        {
            throw new OdinClientException("A drive cannot be owner-only and allow anonymous reads",
                OdinClientErrorCode.CannotAllowAnonymousReadsOnOwnerOnlyDrive);
        }

        if (request.OwnerOnly && request.AllowSubscriptions)
        {
            throw new OdinClientException("A drive cannot be owner-only and allow subscriptions",
                OdinClientErrorCode.CannotAllowSubscriptionsOnOwnerOnlyDrive);
        }

        var mk = odinContext.Caller.GetMasterKey();

        StorageDrive storageDrive;

        using (await _createDriveLock.LockAsync())
        {
            //driveAlias and type must be unique
            if (null != this.GetDriveIdByAlias(request.TargetDrive, db).GetAwaiter().GetResult())
            {
                throw new OdinClientException("Drive alias and type must be unique", OdinClientErrorCode.DriveAliasAndTypeAlreadyExists);
            }

            var driveKey = new SymmetricKeyEncryptedAes(mk);

            var id = Guid.NewGuid();
            var storageKey = driveKey.DecryptKeyClone(mk);

            (byte[] encryptedIdIv, byte[] encryptedIdValue) = AesCbc.Encrypt(id.ToByteArray(), storageKey);

            var sdb = new StorageDriveBase()
            {
                Id = id,
                Name = request.Name,
                TargetDriveInfo = request.TargetDrive,
                Metadata = request.Metadata,
                MasterKeyEncryptedStorageKey = driveKey,
                EncryptedIdIv = encryptedIdIv,
                EncryptedIdValue = encryptedIdValue,
                AllowAnonymousReads = request.AllowAnonymousReads,
                AllowSubscriptions = request.AllowSubscriptions,
                OwnerOnly = request.OwnerOnly,
                Attributes = request.Attributes
            };

            storageKey.Wipe();

            _driveStorage.Upsert(db, sdb.Id, request.TargetDrive.ToKey(), _driveDataType, sdb);

            storageDrive = ToStorageDrive(sdb);
            storageDrive.EnsureDirectories();

            _logger.LogDebug($"Created a new Drive - {storageDrive.TargetDriveInfo}");
            CacheDrive(storageDrive);
            _logger.LogDebug($"End - Created a new Drive - {storageDrive.TargetDriveInfo}");
        }

        await _mediator.Publish(new DriveDefinitionAddedNotification
        {
            IsNewDrive = true,
            Drive = storageDrive,
            OdinContext = odinContext,
            db = db
        });

        return storageDrive;
    }

    public async Task SetDriveReadMode(Guid driveId, bool allowAnonymous, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();
        StorageDrive storageDrive = await GetDrive(driveId, db);

        if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
        {
            throw new OdinSecurityException("Cannot change system drive");
        }

        if (storageDrive.OwnerOnly && allowAnonymous)
        {
            throw new OdinSecurityException("Cannot set Owner Only drive to allow anonymous");
        }

        //only change if needed
        if (storageDrive.AllowAnonymousReads != allowAnonymous)
        {
            storageDrive.AllowAnonymousReads = allowAnonymous;

            _driveStorage.Upsert(db, driveId, storageDrive.TargetDriveInfo.ToKey(), _driveDataType, storageDrive);

            CacheDrive(storageDrive);

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext,
                db = db
            });
        }
    }

    public Task UpdateMetadata(Guid driveId, string metadata, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();

        var sdb = _driveStorage.Get<StorageDriveBase>(db, driveId);
        sdb.Metadata = metadata;

        _driveStorage.Upsert(db, driveId, sdb.TargetDriveInfo.ToKey(), _driveDataType, sdb);

        CacheDrive(ToStorageDrive(sdb));
        return Task.CompletedTask;
    }

    public Task UpdateAttributes(Guid driveId, Dictionary<string, string> attributes, IOdinContext odinContext, IdentityDatabase db)
    {
        odinContext.Caller.AssertHasMasterKey();
        var sdb = _driveStorage.Get<StorageDriveBase>(db, driveId);
        sdb.Attributes = attributes;

        _driveStorage.Upsert(db, driveId, sdb.TargetDriveInfo.ToKey(), _driveDataType, sdb);

        CacheDrive(ToStorageDrive(sdb));
        return Task.CompletedTask;
    }

    public async Task<StorageDrive> GetDrive(Guid driveId, IdentityDatabase db, bool failIfInvalid = false)
    {
        if (_driveCache.TryGetValue(driveId, out var cachedDrive))
        {
            return cachedDrive;
        }

        var sdb = _driveStorage.Get<StorageDriveBase>(db, driveId);
        if (null == sdb)
        {
            if (failIfInvalid)
            {
                throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
            }

            return null;
        }

        var drive = ToStorageDrive(sdb);
        CacheDrive(drive);
        return await Task.FromResult(drive);
    }

    public async Task<Guid?> GetDriveIdByAlias(TargetDrive targetDrive, IdentityDatabase db, bool failIfInvalid = false)
    {
        var cachedDrive = _driveCache.SingleOrDefault(d => d.Value.TargetDriveInfo == targetDrive).Value;
        if (null != cachedDrive)
        {
            return cachedDrive.Id;
        }

        var list = _driveStorage.GetByDataType<StorageDriveBase>(db, targetDrive.ToKey());
        var drives = list as StorageDriveBase[] ?? list.ToArray();
        if (!drives.Any())
        {
            if (failIfInvalid)
            {
                throw new OdinClientException($"Invalid drive id {targetDrive}", OdinClientErrorCode.InvalidTargetDrive);
            }

            return null;
        }

        var drive = ToStorageDrive(drives.Single());
        CacheDrive(drive);
        return await Task.FromResult(drive.Id);
    }

    public async Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions, IOdinContext odinContext, IdentityDatabase db)
    {
        Func<StorageDrive, bool> predicate = drive => true;
        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
        }

        var page = await this.GetDrivesInternal(false, pageOptions, odinContext, db);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;

        // return await this.GetDrivesInternal(true, pageOptions);
    }

    public async Task<PagedResult<StorageDrive>> GetDrives(GuidId type, PageOptions pageOptions, IOdinContext odinContext, IdentityDatabase db)
    {
        Func<StorageDrive, bool> predicate = drive => drive.TargetDriveInfo.Type == type;

        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.TargetDriveInfo.Type == type && drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
        }

        var page = await this.GetDrivesInternal(false, pageOptions, odinContext, db);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    public async Task<PagedResult<StorageDrive>> GetAnonymousDrives(PageOptions pageOptions, IOdinContext odinContext, IdentityDatabase db)
    {
        var page = await this.GetDrivesInternal(false, pageOptions, odinContext, db);
        var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        return results;
    }

    //

    private async Task<PagedResult<StorageDrive>> GetDrivesInternal(bool enforceSecurity, PageOptions pageOptions, IOdinContext odinContext, IdentityDatabase db)
    {
        List<StorageDrive> allDrives;

        if (_driveCache.Any())
        {
            allDrives = _driveCache.Values.ToList();
            _logger.LogTrace($"GetDrivesInternal - cache read:  Count: {allDrives.Count}");
        }
        else
        {
            allDrives = _driveStorage
                .GetByCategory<StorageDriveBase>(db, _driveDataType)
                .Select(ToStorageDrive).ToList();

            _logger.LogTrace($"GetDrivesInternal - disk read:  Count: {allDrives.Count}");
        }

        if (odinContext?.Caller?.IsOwner ?? false)
        {
            return new PagedResult<StorageDrive>(pageOptions, 1, allDrives);
        }

        Func<StorageDriveBase, bool> predicate = null;
        predicate = drive => drive.OwnerOnly == false;
        if (enforceSecurity)
        {
            if (odinContext.Caller.IsAnonymous)
            {
                predicate = drive => drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
            }
        }

        var result = new PagedResult<StorageDrive>(pageOptions, 1, allDrives.Where(predicate).Select(ToStorageDrive).ToList());
        return await Task.FromResult(result);
    }

    private StorageDrive ToStorageDrive(StorageDriveBase sdb)
    {
        //TODO: this should probably go in config
        const string driveFolder = "drives";
        return new StorageDrive(
            Path.Combine(_tenantContext.StorageConfig.TempStoragePath, driveFolder),
            Path.Combine(_tenantContext.StorageConfig.PayloadStoragePath, driveFolder), sdb);
    }

    private void CacheDrive(StorageDrive drive)
    {
        _logger.LogTrace("Cached Drive {drive}", drive.TargetDriveInfo);
        _driveCache[drive.Id] = drive;
    }

    private void LoadCache(IdentityDatabase db)
    {
        var storageDrives = _driveStorage.GetByCategory<StorageDriveBase>(db, _driveDataType);
        foreach (var drive in storageDrives.Select(ToStorageDrive).ToList())
        {
            CacheDrive(drive);
        }
    }
}