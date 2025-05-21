// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading.Tasks;
// using MediatR;
// using Microsoft.Extensions.Logging;
// using Odin.Core;
// using Odin.Core.Cryptography.Crypto;
// using Odin.Core.Cryptography.Data;
// using Odin.Core.Exceptions;
// using Odin.Core.Serialization;
// using Odin.Core.Storage.Database.Identity.Table;
// using Odin.Core.Util;
// using Odin.Services.Authorization.Acl;
// using Odin.Services.Base;
// using Odin.Services.Mediator;
// using Odin.Services.Util;
//
// namespace Odin.Services.Drives.Management;
//
// /// <summary>
// /// Manages drive creation, metadata updates, and their overall definitions
// /// </summary>
// public class DriveManagerRedux : IDriveManager
// {
//     private readonly ILogger<DriveManagerRedux> _logger;
//     private readonly SharedConcurrentDictionary<DriveManagerRedux, Guid, StorageDrive> _driveCache;
//     private readonly SharedAsyncLock<DriveManagerRedux> _createDriveLock;
//     private readonly IMediator _mediator;
//     private readonly TenantContext _tenantContext;
//     private readonly TableKeyThreeValue _tblKeyThreeValue;
//     private readonly TableDriveDefinitions _tableDriveDefinitions;
//
//     /// <summary>
//     /// Manages drive creation, metadata updates, and their overall definitions
//     /// </summary>
//     public DriveManagerRedux(ILogger<DriveManagerRedux> logger,
//         SharedConcurrentDictionary<DriveManagerRedux, Guid, StorageDrive> driveCache,
//         SharedAsyncLock<DriveManagerRedux> createDriveLock,
//         IMediator mediator,
//         TenantContext tenantContext,
//         TableKeyThreeValue tblKeyThreeValue,
//         TableDriveDefinitions tableDriveDefinitions)
//     {
//         _logger = logger;
//         _driveCache = driveCache;
//         _createDriveLock = createDriveLock;
//         _mediator = mediator;
//         _tenantContext = tenantContext;
//         _tblKeyThreeValue = tblKeyThreeValue;
//         _tableDriveDefinitions = tableDriveDefinitions;
//     }
//
//     public async Task<StorageDrive> CreateDriveAsync(CreateDriveRequest request, IOdinContext odinContext)
//     {
//         if (string.IsNullOrEmpty(request?.Name))
//         {
//             throw new OdinClientException("Name cannot be empty");
//         }
//
//         OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
//
//         if (request.OwnerOnly && request.AllowAnonymousReads)
//         {
//             throw new OdinClientException("A drive cannot be owner-only and allow anonymous reads",
//                 OdinClientErrorCode.CannotAllowAnonymousReadsOnOwnerOnlyDrive);
//         }
//
//         if (request.OwnerOnly && request.AllowSubscriptions)
//         {
//             throw new OdinClientException("A drive cannot be owner-only and allow subscriptions",
//                 OdinClientErrorCode.CannotAllowSubscriptionsOnOwnerOnlyDrive);
//         }
//
//         var mk = odinContext.Caller.GetMasterKey();
//
//         StorageDrive storageDrive;
//
//         // SEB:TODO does not scale
//         using (await _createDriveLock.LockAsync())
//         {
//             var driveKey = new SymmetricKeyEncryptedAes(mk);
//
//             var id = request.TargetDrive.Alias.Value;
//             var storageKey = driveKey.DecryptKeyClone(mk);
//
//             (byte[] encryptedIdIv, byte[] encryptedIdValue) = AesCbc.Encrypt(id.ToByteArray(), storageKey);
//
//             var sdb = new StorageDriveDefinition()
//             {
//                 Name = request.Name,
//                 TargetDriveInfo = request.TargetDrive,
//                 Metadata = request.Metadata,
//                 MasterKeyEncryptedStorageKey = driveKey,
//                 EncryptedIdIv = encryptedIdIv,
//                 EncryptedIdValue = encryptedIdValue,
//                 AllowAnonymousReads = request.AllowAnonymousReads,
//                 AllowSubscriptions = request.AllowSubscriptions,
//                 OwnerOnly = request.OwnerOnly,
//                 Attributes = request.Attributes
//             };
//
//             await _tableDriveDefinitions.UpsertAsync(new DriveDefinitionsRecord
//             {
//                 driveId = id,
//                 driveType = request.TargetDrive.Type.Value,
//                 data = OdinSystemSerializer.Serialize(sdb)
//             });
//
//             storageKey.Wipe();
//
//             storageDrive = ToStorageDrive(sdb);
//             storageDrive.EnsureDirectories();
//
//             _logger.LogDebug($"Created a new Drive - {storageDrive.TargetDriveInfo}");
//             CacheDrive(storageDrive);
//             _logger.LogDebug($"End - Created a new Drive - {storageDrive.TargetDriveInfo}");
//         }
//
//         await _mediator.Publish(new DriveDefinitionAddedNotification
//         {
//             IsNewDrive = true,
//             Drive = storageDrive,
//             OdinContext = odinContext,
//         });
//
//         return storageDrive;
//     }
//
//     public async Task SetDriveReadModeAsync(Guid driveId, bool allowAnonymous, IOdinContext odinContext)
//     {
//         odinContext.Caller.AssertHasMasterKey();
//         StorageDrive storageDrive = await GetDriveAsync(driveId);
//
//         if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
//         {
//             throw new OdinSecurityException("Cannot change system drive");
//         }
//
//         if (storageDrive.OwnerOnly && allowAnonymous)
//         {
//             throw new OdinSecurityException("Cannot set Owner Only drive to allow anonymous");
//         }
//
//         //only change if needed
//         if (storageDrive.AllowAnonymousReads != allowAnonymous)
//         {
//             storageDrive.AllowAnonymousReads = allowAnonymous;
//
//             await _tableDriveDefinitions.UpsertAsync(new DriveDefinitionsRecord
//             {
//                 driveId = driveId,
//                 driveType = storageDrive.TargetDriveInfo.Type,
//                 data = OdinSystemSerializer.Serialize(storageDrive)
//             });
//
//             CacheDrive(storageDrive);
//
//             await _mediator.Publish(new DriveDefinitionAddedNotification
//             {
//                 IsNewDrive = false,
//                 Drive = storageDrive,
//                 OdinContext = odinContext,
//             });
//         }
//     }
//
//     public async Task SetDriveAllowSubscriptionsAsync(Guid driveId, bool allowSubscriptions, IOdinContext odinContext)
//     {
//         odinContext.Caller.AssertHasMasterKey();
//         StorageDrive storageDrive = await GetDriveAsync(driveId);
//
//         if (SystemDriveConstants.SystemDrives.Any(d => d == storageDrive.TargetDriveInfo))
//         {
//             throw new OdinSecurityException("Cannot change system drive");
//         }
//
//         if (storageDrive.OwnerOnly && allowSubscriptions)
//         {
//             throw new OdinSecurityException("Cannot set Owner Only drive to allow anonymous");
//         }
//
//         //only change if needed
//         if (storageDrive.AllowSubscriptions != allowSubscriptions)
//         {
//             storageDrive.AllowSubscriptions = allowSubscriptions;
//
//             await _tableDriveDefinitions.UpsertAsync(new DriveDefinitionsRecord
//             {
//                 driveId = driveId,
//                 driveType = storageDrive.TargetDriveInfo.Type,
//                 data = OdinSystemSerializer.Serialize(storageDrive)
//             });
//
//             CacheDrive(storageDrive);
//
//             await _mediator.Publish(new DriveDefinitionAddedNotification
//             {
//                 IsNewDrive = false,
//                 Drive = storageDrive,
//                 OdinContext = odinContext
//             });
//         }
//     }
//
//     public async Task UpdateMetadataAsync(Guid driveId, string metadata, IOdinContext odinContext)
//     {
//         odinContext.Caller.AssertHasMasterKey();
//
//         // _tableDriveDefinitions.GetAsync(driveId)
//         // var sdb = await DriveStorage.GetAsync<StorageDriveBase>(_tblKeyThreeValue, driveId);
//         // sdb.Metadata = metadata;
//
//         // await DriveStorage.UpsertAsync(_tblKeyThreeValue, driveId, sdb.TargetDriveInfo.ToKey(), DriveDataType, sdb);
//
//         // CacheDrive(ToStorageDrive(sdb));
//     }
//
//     public async Task UpdateAttributesAsync(Guid driveId, Dictionary<string, string> attributes, IOdinContext odinContext)
//     {
//         odinContext.Caller.AssertHasMasterKey();
//         var sdb = await DriveStorage.GetAsync<StorageDriveBase>(_tblKeyThreeValue, driveId);
//         sdb.Attributes = attributes;
//
//         await DriveStorage.UpsertAsync(_tblKeyThreeValue, driveId, sdb.TargetDriveInfo.ToKey(), DriveDataType, sdb);
//
//         CacheDrive(ToStorageDrive(sdb));
//     }
//
//     public async Task<StorageDrive> GetDriveAsync(Guid driveId, bool failIfInvalid = false)
//     {
//         if (_driveCache.TryGetValue(driveId, out var cachedDrive))
//         {
//             return cachedDrive;
//         }
//
//         var sdb = await DriveStorage.GetAsync<StorageDriveBase>(_tblKeyThreeValue, driveId);
//         if (null == sdb)
//         {
//             if (failIfInvalid)
//             {
//                 throw new OdinClientException($"Invalid drive id {driveId}", OdinClientErrorCode.InvalidDrive);
//             }
//
//             return null;
//         }
//
//         var drive = ToStorageDrive(sdb);
//         CacheDrive(drive);
//         return drive;
//     }
//
//     public async Task<StorageDrive> GetDriveAsync(TargetDrive targetDrive, bool failIfInvalid = false)
//     {
//         var driveId = targetDrive.Alias;
//         return await GetDriveAsync(driveId, failIfInvalid);
//     }
//
//     public async Task<Guid?> GetDriveIdByAliasAsync(TargetDrive targetDrive, bool failIfInvalid = false)
//     {
//         // kill this method later
//         await Task.CompletedTask;
//         return targetDrive.Alias;
//     }
//
//     public async Task<PagedResult<StorageDrive>> GetDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
//     {
//         Func<StorageDrive, bool> predicate = drive => true;
//         if (odinContext.Caller.IsAnonymous)
//         {
//             predicate = drive => drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
//         }
//
//         var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
//         var storageDrives = page.Results.Where(predicate).ToList();
//         var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
//         return results;
//
//         // return await this.GetDrivesInternal(true, pageOptions);
//     }
//
//     public async Task<PagedResult<StorageDrive>> GetDrivesAsync(GuidId type, PageOptions pageOptions, IOdinContext odinContext)
//     {
//         Func<StorageDrive, bool> predicate = drive => drive.TargetDriveInfo.Type == type;
//
//         if (odinContext.Caller.IsAnonymous)
//         {
//             predicate = drive => drive.TargetDriveInfo.Type == type && drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
//         }
//
//         var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
//         var storageDrives = page.Results.Where(predicate).ToList();
//         var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
//         return results;
//     }
//
//     public async Task<PagedResult<StorageDrive>> GetAnonymousDrivesAsync(PageOptions pageOptions, IOdinContext odinContext)
//     {
//         var page = await this.GetDrivesInternalAsync(false, pageOptions, odinContext);
//         var storageDrives = page.Results.Where(drive => drive.AllowAnonymousReads).ToList();
//         var results = new PagedResult<StorageDrive>(pageOptions, 1, storageDrives);
//         return results;
//     }
//
//     //
//
//     private async Task<PagedResult<StorageDrive>> GetDrivesInternalAsync(bool enforceSecurity, PageOptions pageOptions,
//         IOdinContext odinContext)
//     {
//         List<StorageDrive> allDrives;
//
//         if (_driveCache.Any())
//         {
//             allDrives = _driveCache.Values.ToList();
//             _logger.LogTrace($"GetDrivesInternal - cache read:  Count: {allDrives.Count}");
//         }
//         else
//         {
//             var d = await DriveStorage.GetByCategoryAsync<StorageDriveBase>(_tblKeyThreeValue, DriveDataType);
//             allDrives = d.Select(ToStorageDrive).ToList();
//             _logger.LogTrace($"GetDrivesInternal - disk read:  Count: {allDrives.Count}");
//         }
//
//         if (odinContext?.Caller?.IsOwner ?? false)
//         {
//             return new PagedResult<StorageDrive>(pageOptions, 1, allDrives);
//         }
//
//         Func<StorageDriveBase, bool> predicate = null;
//         predicate = drive => drive.OwnerOnly == false;
//         if (enforceSecurity)
//         {
//             if (odinContext.Caller.IsAnonymous)
//             {
//                 predicate = drive => drive.AllowAnonymousReads == true && drive.OwnerOnly == false;
//             }
//         }
//
//         var result = new PagedResult<StorageDrive>(pageOptions, 1, allDrives.Where(predicate).Select(ToStorageDrive).ToList());
//         return result;
//     }
//
//     private StorageDrive ToStorageDrive(StorageDriveBase sdb)
//     {
//         return new StorageDrive(_tenantContext.TenantPathManager, sdb);
//     }
//
//     private void CacheDrive(StorageDrive drive)
//     {
//         _logger.LogTrace("Cached Drive {drive}", drive.TargetDriveInfo);
//         _driveCache[drive.Id] = drive;
//     }
//
//     public async Task LoadCacheAsync()
//     {
//         var storageDrives = await DriveStorage.GetByCategoryAsync<StorageDriveBase>(_tblKeyThreeValue, DriveDataType);
//         foreach (var drive in storageDrives.Select(ToStorageDrive).ToList())
//         {
//             CacheDrive(drive);
//         }
//     }
// }