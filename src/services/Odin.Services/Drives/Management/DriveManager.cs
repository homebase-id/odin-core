using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Util;
using Odin.Services.Base;
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
    internal static readonly Guid DriveContextKey = Guid.Parse("4cca76c6-3432-4372-bef8-5f05313c0376");
    private static readonly ThreeKeyValueStorage DriveStorage = TenantSystemStorage.CreateThreeKeyValueStorage(DriveContextKey);
    internal static readonly byte[] DriveDataType = "drive".ToUtf8ByteArray(); //keep it lower case

    private readonly ILogger<DriveManager> _logger;
    private readonly IMediator _mediator;

    // SEB:NOTE we can't use LxCache here since multiple key and list retrieval
    // are required, so we leave _driveCache for now
    private readonly SharedConcurrentDictionary<DriveManager, Guid, StorageDrive> _driveCache;
    private readonly SharedAsyncLock<DriveManager> _createDriveLock;

    private readonly TenantContext _tenantContext;
    private readonly TableKeyThreeValue _tblKeyThreeValue;

    private readonly DriveManagerWithDedicatedTable _driveWithDedicatedTable;

    public DriveManager(
        ILogger<DriveManager> logger,
        SharedConcurrentDictionary<DriveManager, Guid, StorageDrive> driveCache,
        SharedAsyncLock<DriveManager> createDriveLock,
        IMediator mediator,
        TenantContext tenantContext,
        TableKeyThreeValue tblKeyThreeValue,
        DriveManagerWithDedicatedTable driveWithDedicatedTable)
    {
        _logger = logger;
        _driveCache = driveCache;
        _createDriveLock = createDriveLock;
        _mediator = mediator;
        _tenantContext = tenantContext;
        _tblKeyThreeValue = tblKeyThreeValue;
        _driveWithDedicatedTable = driveWithDedicatedTable;
    }

    public async Task<StorageDrive> CreateDriveAsync(CreateDriveRequest request, IOdinContext odinContext)
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
        var id = Guid.NewGuid();

        // SEB:TODO does not scale
        using (await _createDriveLock.LockAsync())
        {
            //driveAlias and type must be unique
            if (null != await this.GetDriveIdByAliasAsync(request.TargetDrive))
            {
                throw new OdinClientException("Drive alias and type must be unique", OdinClientErrorCode.DriveAliasAndTypeAlreadyExists);
            }

            var driveKey = new SymmetricKeyEncryptedAes(mk);
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

            await DriveStorage.UpsertAsync(_tblKeyThreeValue, sdb.Id, request.TargetDrive.ToKey(), DriveDataType, sdb);

            storageDrive = ToStorageDrive(sdb);
            storageDrive.EnsureDirectories();

            _logger.LogDebug($"Created a new Drive - {storageDrive.TargetDriveInfo}");
            CacheDrive(storageDrive);
            _logger.LogDebug($"End - Created a new Drive - {storageDrive.TargetDriveInfo}");
            
            try
            {
                // await _driveWithDedicatedTable.CreateDriveAsync(id, request, odinContext);
                await _driveWithDedicatedTable.CreateDriveFromClassicDriveManagerAsync(sdb);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, $"Failure while creating drive with {nameof(_driveWithDedicatedTable)}");
            }
        }

        await _mediator.Publish(new DriveDefinitionAddedNotification
        {
            IsNewDrive = true,
            Drive = storageDrive,
            OdinContext = odinContext,
        });

        return storageDrive;
    }

    public async Task SetDriveReadModeAsync(Guid driveId, bool allowAnonymous, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        StorageDrive storageDrive = await GetDriveAsync(driveId);

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

            await DriveStorage.UpsertAsync(_tblKeyThreeValue, driveId, storageDrive.TargetDriveInfo.ToKey(), DriveDataType, storageDrive);

            CacheDrive(storageDrive);

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext,
            });
        }

        try
        {
            await _driveWithDedicatedTable.SetDriveReadModeAsync(driveId, allowAnonymous, odinContext);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Failure while calling _driveWithDedicatedTable.SetDriveReadModeAsync");
        }
    }

    public async Task SetDriveAllowSubscriptionsAsync(Guid driveId, bool allowSubscriptions, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        StorageDrive storageDrive = await GetDriveAsync(driveId);

        if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
        {
            throw new OdinSecurityException("Cannot change system drive");
        }

        if (storageDrive.OwnerOnly && allowSubscriptions)
        {
            throw new OdinSecurityException("Cannot set Owner Only drive to allow anonymous");
        }

        //only change if needed
        if (storageDrive.AllowSubscriptions != allowSubscriptions)
        {
            storageDrive.AllowSubscriptions = allowSubscriptions;

            await DriveStorage.UpsertAsync(_tblKeyThreeValue, driveId, storageDrive.TargetDriveInfo.ToKey(), DriveDataType, storageDrive);

            CacheDrive(storageDrive);

            await _mediator.Publish(new DriveDefinitionAddedNotification
            {
                IsNewDrive = false,
                Drive = storageDrive,
                OdinContext = odinContext
            });
        }

        try
        {
            await _driveWithDedicatedTable.SetDriveAllowSubscriptionsAsync(driveId, allowSubscriptions, odinContext);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Failure while calling _driveWithDedicatedTable.SetDriveAllowSubscriptionsAsync");
            throw;
        }
    }

    public async Task UpdateMetadataAsync(Guid driveId, string metadata, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var sdb = await DriveStorage.GetAsync<StorageDriveBase>(_tblKeyThreeValue, driveId);
        sdb.Metadata = metadata;

        await DriveStorage.UpsertAsync(_tblKeyThreeValue, driveId, sdb.TargetDriveInfo.ToKey(), DriveDataType, sdb);

        CacheDrive(ToStorageDrive(sdb));

        try
        {
            await _driveWithDedicatedTable.UpdateMetadataAsync(driveId, metadata, odinContext);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Failure while calling _driveWithDedicatedTable.UpdateMetadataAsync");
        }
    }

    public async Task UpdateAttributesAsync(Guid driveId, Dictionary<string, string> attributes, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        var sdb = await DriveStorage.GetAsync<StorageDriveBase>(_tblKeyThreeValue, driveId);
        sdb.Attributes = attributes;

        await DriveStorage.UpsertAsync(_tblKeyThreeValue, driveId, sdb.TargetDriveInfo.ToKey(), DriveDataType, sdb);

        CacheDrive(ToStorageDrive(sdb));

        try
        {
            await _driveWithDedicatedTable.UpdateAttributesAsync(driveId, attributes, odinContext);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Failure while calling _driveWithDedicatedTable.UpdateAttributesAsync");
        }
    }

    public async Task<StorageDrive> GetDriveAsync(Guid driveId, bool failIfInvalid = false)
    {
        if (_driveCache.TryGetValue(driveId, out var cachedDrive))
        {
            return cachedDrive;
        }

        var sdb = await DriveStorage.GetAsync<StorageDriveBase>(_tblKeyThreeValue, driveId);
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

        try
        {
            var theSecondaryDrive = await _driveWithDedicatedTable.GetDriveAsync(driveId, failIfInvalid);

            if (!StorageDriveComparer.AreEqual(drive, theSecondaryDrive, out var difference))
            {
                _logger.LogWarning("Found differences between primary drive {d1} and the secondary drive {d1}.  Diffs are: {difference}",
                    drive.Name, theSecondaryDrive.Name, difference);
            }
            else
            {
                _logger.LogInformation("GetDriveAsync (by driveid) is valid");
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failure while calling _driveWithDedicatedTable.GetDriveAsync(driveId)");
        }

        return drive;
    }

    public async Task<StorageDrive> GetDriveAsync(TargetDrive targetDrive, bool failIfInvalid = false)
    {
        var driveId = await GetDriveIdByAliasAsync(targetDrive, failIfInvalid);
        var drive =  await GetDriveAsync(driveId.GetValueOrDefault(), failIfInvalid);

        try
        {
            var theSecondaryDrive = await _driveWithDedicatedTable.GetDriveAsync(targetDrive, failIfInvalid);

            if (!StorageDriveComparer.AreEqual(drive, theSecondaryDrive, out var difference))
            {
                _logger.LogWarning("GetDriveAsync (by target drive) Found differences between primary drive {d1} and " +
                                   "the secondary drive {d1}.  Diffs are: {difference}", 
                    drive.Name, theSecondaryDrive.Name, difference);
            }
            else
            {
                _logger.LogInformation("GetDriveAsync (by target drive) is valid");
            }
            
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failure while calling _driveWithDedicatedTable.GetDriveAsync(targetDrive)");
        }
        
        return drive;
    }

    public async Task<Guid?> GetDriveIdByAliasAsync(TargetDrive targetDrive, bool failIfInvalid = false)
    {
        var cachedDrive = _driveCache.SingleOrDefault(d => d.Value.TargetDriveInfo == targetDrive).Value;
        if (null != cachedDrive)
        {
            return cachedDrive.Id;
        }

        var list = await DriveStorage.GetByDataTypeAsync<StorageDriveBase>(_tblKeyThreeValue, targetDrive.ToKey());
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
        return drive.Id;
    }

    public async Task<PagedResult<StorageDrive>> GetDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        Func<StorageDrive, bool> predicate = drive => true;
        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
        }

        var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        
        try
        {
            var theSecondaryList = await _driveWithDedicatedTable.GetDrivesAsync(pageOptions, odinContext);
            var result = StorageDriveComparer.CompareLists(results.Results.ToList(), theSecondaryList.Results.ToList());
            if (result.OnlyInFirst.Any() || result.OnlyInSecond.Any() || result.Mismatched.Any())
            {
                foreach (var d in result.OnlyInFirst)
                    _logger.LogWarning($"Only in first list: {d.Id}");

                foreach (var d in result.OnlyInSecond)
                    _logger.LogWarning($"Only in second list: {d.Id}");

                foreach (var (d1, d2, diffs) in result.Mismatched)
                {
                    _logger.LogWarning($"Mismatched ID {d1.Id}:");
                    foreach (var diff in diffs)
                    {
                        _logger.LogWarning($"  - {diff}");
                    }
                }
            }
            else
            {
                _logger.LogInformation("GetDrivesAsync - all drives valid");
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failure while calling _driveWithDedicatedTable.GetDrivesAsync");
        }
        
        return results;

        // return await this.GetDrivesInternal(true, pageOptions);
    }

    public async Task<PagedResult<StorageDrive>> GetDrivesAsync(GuidId type, PageOptions pageOptions, IOdinContext odinContext)
    {
        Func<StorageDrive, bool> predicate = drive => drive.TargetDriveInfo.Type == type;

        if (odinContext.Caller.IsAnonymous)
        {
            predicate = drive => drive.TargetDriveInfo.Type == type && drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
        }

        var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(predicate).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        
        try
        {
            var theSecondaryList = await _driveWithDedicatedTable.GetDrivesAsync(type, pageOptions, odinContext);
            var result = StorageDriveComparer.CompareLists(results.Results.ToList(), theSecondaryList.Results.ToList());
            if (result.OnlyInFirst.Any() || result.OnlyInSecond.Any() || result.Mismatched.Any())
            {
                foreach (var d in result.OnlyInFirst)
                    _logger.LogWarning($"Only in first list: {d.Id}");

                foreach (var d in result.OnlyInSecond)
                    _logger.LogWarning($"Only in second list: {d.Id}");

                foreach (var (d1, d2, diffs) in result.Mismatched)
                {
                    _logger.LogWarning($"Mismatched ID {d1.Id}:");
                    foreach (var diff in diffs)
                    {
                        _logger.LogWarning($"  - {diff}");
                    }
                }
            }
            else
            {
                _logger.LogInformation("GetDrivesAsync (type) - all drives valid");
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failure while calling _driveWithDedicatedTable.GetDrivesAsync(type)");
        }
        
        return results;
    }

    public async Task<PagedResult<StorageDrive>> GetAnonymousDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
    {
        var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
        var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads).ToList();
        var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
        
        try
        {
            var theSecondaryList = await _driveWithDedicatedTable.GetAnonymousDrivesAsync(pageOptions, odinContext);
            var result = StorageDriveComparer.CompareLists(results.Results.ToList(), theSecondaryList.Results.ToList());
            if (result.OnlyInFirst.Any() || result.OnlyInSecond.Any() || result.Mismatched.Any())
            {
                foreach (var d in result.OnlyInFirst)
                    _logger.LogWarning($"Only in first list: {d.Id}");

                foreach (var d in result.OnlyInSecond)
                    _logger.LogWarning($"Only in second list: {d.Id}");

                foreach (var (d1, d2, diffs) in result.Mismatched)
                {
                    _logger.LogWarning($"Mismatched ID {d1.Id}:");
                    foreach (var diff in diffs)
                    {
                        _logger.LogWarning($"  - {diff}");
                    }
                }
            }
            else
            {
                _logger.LogInformation("GetAnonymousDrivesAsync - all drives valid");
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failure while calling _driveWithDedicatedTable.GetDrivesAsync(type)");
        }

        
        return results;
    }

    //

    private async Task<PagedResult<StorageDrive>> GetDrivesInternalAsync(bool enforceSecurity, PageOptions pageOptions,
        IOdinContext odinContext)
    {
        List<StorageDrive> allDrives;

        if (_driveCache.Any())
        {
            allDrives = _driveCache.Values.ToList();
            _logger.LogTrace($"GetDrivesInternal - cache read:  Count: {allDrives.Count}");
        }
        else
        {
            var d = await DriveStorage.GetByCategoryAsync<StorageDriveBase>(_tblKeyThreeValue, DriveDataType);
            allDrives = d.Select(ToStorageDrive).ToList();
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
        return result;
    }

    private StorageDrive ToStorageDrive(StorageDriveBase sdb)
    {
        return new StorageDrive(_tenantContext.TenantPathManager, sdb);
    }

    private void CacheDrive(StorageDrive drive)
    {
        _logger.LogTrace("Cached Drive {drive}", drive.TargetDriveInfo);
        _driveCache[drive.Id] = drive;
    }

    public async Task LoadCacheAsync()
    {
        var storageDrives = await DriveStorage.GetByCategoryAsync<StorageDriveBase>(_tblKeyThreeValue, DriveDataType);
        foreach (var drive in storageDrives.Select(ToStorageDrive).ToList())
        {
            CacheDrive(drive);
        }
    }
}