using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.Eula;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Registry;
using Odin.Services.Util;

namespace Odin.Services.Configuration;

/// <summary>
/// Manages initial setup and system configuration for the identity and owner-app
/// </summary>
public class TenantConfigService
{
    private readonly CircleNetworkService _dbs;
    private readonly TenantSystemStorage _tenantSystemStorage;

    private readonly TenantContext _tenantContext;
    private readonly SingleKeyValueStorage _configStorage;
    private readonly IIdentityRegistry _registry;
    private readonly DriveManager _driveManager;
    private readonly PublicPrivateKeyService _publicPrivateKeyService;
    private readonly RecoveryService _recoverService;
    private readonly IcrKeyService _icrKeyService;
    private readonly CircleMembershipService _circleMembershipService;
    private readonly IAppRegistrationService _appRegistrationService;

    public TenantConfigService(CircleNetworkService dbs,
        TenantSystemStorage storage,
        TenantContext tenantContext,
        IIdentityRegistry registry,
        DriveManager driveManager,
        PublicPrivateKeyService publicPrivateKeyService,
        IcrKeyService icrKeyService,
        RecoveryService recoverService,
        CircleMembershipService circleMembershipService,
        IAppRegistrationService appRegistrationService)
    {
        _dbs = dbs;
        _tenantSystemStorage = storage;

        _tenantContext = tenantContext;
        _registry = registry;
        _driveManager = driveManager;
        _publicPrivateKeyService = publicPrivateKeyService;
        _recoverService = recoverService;
        _circleMembershipService = circleMembershipService;
        _appRegistrationService = appRegistrationService;
        _icrKeyService = icrKeyService;

        const string configContextKey = "b9e1c2a3-e0e0-480e-a696-ce602b052d07";
        _configStorage = storage.CreateSingleKeyValueStorage(Guid.Parse(configContextKey));

        _tenantContext.UpdateSystemConfig(GetTenantSettings());
    }

    public bool IsIdentityServerConfigured()
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = _configStorage.Get<FirstRunInfo>(db, FirstRunInfo.Key);
        return firstRunInfo != null;
    }

    public bool IsEulaSignatureRequired(IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();

        var info = _configStorage.Get<List<EulaSignature>>(db, EulaSystemInfo.StorageKey);
        if (info == null || !info.Any())
        {
            return true;
        }

        var signature = info.SingleOrDefault(signature => signature.Version == EulaSystemInfo.RequiredVersion);
        return signature == null;
    }

    public EulaVersionResponse GetRequiredEulaVersion(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        return new EulaVersionResponse()
        {
            Version = EulaSystemInfo.RequiredVersion
        };
    }

    public List<EulaSignature> GetEulaSignatureHistory(IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();

        var signatures = _configStorage.Get<List<EulaSignature>>(db, EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        return signatures;
    }

    public void MarkEulaSigned(MarkEulaSignedRequest request, IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();

        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Version, nameof(request.Version));

        if (request.Version != EulaSystemInfo.RequiredVersion)
        {
            throw new OdinClientException("Invalid Eula version");
        }

        var signatures = _configStorage.Get<List<EulaSignature>>(db, EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        signatures.Add(new EulaSignature()
        {
            SignatureDate = UnixTimeUtc.Now(),
            Version = request.Version,
            SignatureBytes = request.SignatureBytes
        });

        _configStorage.Upsert(db, Eula.EulaSystemInfo.StorageKey, signatures);
    }

    public async Task CreateInitialKeys(IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();

        await _recoverService.CreateInitialKey(odinContext);

        await _publicPrivateKeyService.CreateInitialKeys(odinContext, db);

        await _icrKeyService.CreateInitialKeys(odinContext);
    }

    /// <summary>
    /// Configures aspects of the owner's identity that require the master key
    /// </summary>
    public async Task EnsureInitialOwnerSetup(InitialSetupRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (request.FirstRunToken.HasValue)
        {
            await _registry.MarkRegistrationComplete(request.FirstRunToken.GetValueOrDefault());
        }

        //Note: the order here is important.  if the request or system drives include any anonymous
        //drives, they should be added after the system circle exists
        await _circleMembershipService.CreateSystemCircle(odinContext);

        await CreateDriveIfNotExists(SystemDriveConstants.CreateChatDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateMailDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateFeedDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateHomePageConfigDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreatePublicPostsChannelDriveRequest, odinContext);

        await CreateDriveIfNotExists(SystemDriveConstants.CreateContactDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateProfileDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateWalletDriveRequest, odinContext);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateTransientTempDriveRequest, odinContext);

        foreach (var rd in request.Drives ?? new List<CreateDriveRequest>())
        {
            await CreateDriveIfNotExists(rd, odinContext);
        }

        //Create additional circles last in case they rely on any of the drives above
        foreach (var rc in request.Circles ?? new List<CreateCircleRequest>())
        {
            await CreateCircleIfNotExists(rc, odinContext);
        }

        await this.RegisterBuiltInApps(odinContext);

        var db = _tenantSystemStorage.IdentityDatabase;

        // TODO CONNECTIONS
        // db.CreateCommitUnitOfWork(() => {
        // _configStorage.Upsert(db, TenantSettings.ConfigKey, TenantSettings.Default);
        // _configStorage.Upsert(db, FirstRunInfo.Key, new FirstRunInfo()
        // {
        //    FirstRunDate = UnixTimeUtc.Now().milliseconds
        // });
        // });

        var keyValuePairs = new List<(Guid key, object value)>
        {
            (TenantSettings.ConfigKey, TenantSettings.Default),
            (FirstRunInfo.Key, new FirstRunInfo() { FirstRunDate = UnixTimeUtc.Now().milliseconds })
        };

        _configStorage.UpsertMany(db, keyValuePairs);
    }

    public async Task UpdateSystemFlag(UpdateFlagRequest request, IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();

        if (!Enum.TryParse(typeof(TenantConfigFlagNames), request.FlagName, true, out var flag))
        {
            throw new OdinClientException("Invalid flag name", OdinClientErrorCode.InvalidFlagName);
        }

        var cfg = _configStorage.Get<TenantSettings>(db, TenantSettings.ConfigKey) ?? new TenantSettings();

        switch (flag)
        {
            case TenantConfigFlagNames.AnonymousVisitorsCanViewWhoIFollow:
                cfg.AnonymousVisitorsCanViewWhoIFollow = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewWhoIFollow:
                cfg.AuthenticatedIdentitiesCanViewWhoIFollow = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewWhoIFollow:
                cfg.AllConnectedIdentitiesCanViewWhoIFollow = bool.Parse(request.Value);
                await UpdateSystemCirclePermission(PermissionKeys.ReadWhoIFollow, cfg.AllConnectedIdentitiesCanViewWhoIFollow, odinContext);
                break;

            case TenantConfigFlagNames.AnonymousVisitorsCanViewConnections:
                cfg.AnonymousVisitorsCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewConnections:
                cfg.AuthenticatedIdentitiesCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections:
                cfg.AllConnectedIdentitiesCanViewConnections = bool.Parse(request.Value);
                await UpdateSystemCirclePermission(PermissionKeys.ReadConnections, cfg.AllConnectedIdentitiesCanViewConnections, odinContext);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanReactOnAnonymousDrives:
                cfg.AuthenticatedIdentitiesCanReactOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanCommentOnAnonymousDrives:
                cfg.AuthenticatedIdentitiesCanCommentOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanReactOnAnonymousDrives:
                cfg.ConnectedIdentitiesCanReactOnAnonymousDrives = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanCommentOnAnonymousDrives:
                cfg.ConnectedIdentitiesCanCommentOnAnonymousDrives = bool.Parse(request.Value);
                break;

            default:
                throw new OdinClientException("Flag name is valid but not handled",
                    OdinClientErrorCode.UnknownFlagName);
        }

        _configStorage.Upsert(db, TenantSettings.ConfigKey, cfg);

        //TODO: eww, use mediator instead
        _tenantContext.UpdateSystemConfig(cfg);
    }

    public TenantSettings GetTenantSettings()
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        return _configStorage.Get<TenantSettings>(db, TenantSettings.ConfigKey) ?? TenantSettings.Default;
    }

    public OwnerAppSettings GetOwnerAppSettings(IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();
        return _configStorage.Get<OwnerAppSettings>(db, OwnerAppSettings.ConfigKey) ?? OwnerAppSettings.Default;
    }

    public void UpdateOwnerAppSettings(OwnerAppSettings newSettings, IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        odinContext.Caller.AssertHasMasterKey();
        _configStorage.Upsert(db, OwnerAppSettings.ConfigKey, newSettings);
    }

    //

    private async Task RegisterBuiltInApps(IOdinContext odinContext)
    {
        await RegisterChatApp(odinContext);
        await RegisterMailApp(odinContext);
        await RegisterFeedApp(odinContext);
        // await RegisterPhotosApp();
    }

    private async Task RegisterFeedApp(IOdinContext odinContext)
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.FeedAppId,
            Name = "Homebase - Feed",
            AuthorizedCircles = new List<Guid>(),
            CircleMemberPermissionGrant = null,
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.FeedDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.HomePageConfigDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.PublicPostsChannelDrive,
                        Permission = DrivePermission.All
                    }
                }
            ],
            PermissionSet = new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ReadWhoIFollow,
                PermissionKeys.ReadMyFollowers,
                PermissionKeys.ManageFeed,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.UseTransitRead,
                PermissionKeys.PublishStaticContent,
                PermissionKeys.UseTransitWrite)
        };

        await _appRegistrationService.RegisterApp(request, odinContext);
    }


    private async Task RegisterChatApp(IOdinContext odinContext)
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.ChatAppId,
            Name = "Homebase - Chat",
            AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
            {
                SystemCircleConstants.ConnectedIdentitiesSystemCircleId
            },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest()
            {
                Drives =
                [
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = SystemDriveConstants.ChatDrive,
                            Permission = DrivePermission.Write  | DrivePermission.React
                        }
                    }
                ],
                PermissionSet = new PermissionSet()

                // PermissionSet = new PermissionSet(
                //     PermissionKeys.ReadConnections,
                //     PermissionKeys.SendPushNotifications,
                //     PermissionKeys.UseTransitRead,
                //     PermissionKeys.UseTransitWrite)
            },
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ChatDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            ],
            PermissionSet = new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ReadConnectionRequests,
                // PermissionKeys.UseTransitRead,
                PermissionKeys.UseTransitWrite)
        };

        await _appRegistrationService.RegisterApp(request, odinContext);
    }

    private async Task RegisterMailApp(IOdinContext odinContext)
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.MailAppId,
            Name = "Homebase - Mail",
            AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
            {
                SystemCircleConstants.ConnectedIdentitiesSystemCircleId
            },
            CircleMemberPermissionGrant = new PermissionSetGrantRequest()
            {
                Drives =
                [
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = SystemDriveConstants.MailDrive,
                            Permission = DrivePermission.Write
                        }
                    }
                ],
                PermissionSet = new PermissionSet()
            },
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.MailDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.Read
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            ],
            PermissionSet = new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.UseTransitWrite)
        };

        await _appRegistrationService.RegisterApp(request, odinContext);
    }

    private async Task<bool> CreateCircleIfNotExists(CreateCircleRequest request, IOdinContext odinContext)
    {
        var existingCircleDef = _circleMembershipService.GetCircle(request.Id, odinContext);
        if (null == existingCircleDef)
        {
            await _circleMembershipService.CreateCircleDefinition(request, odinContext);
            return true;
        }

        return false;
    }

    private async Task<bool> CreateDriveIfNotExists(CreateDriveRequest request, IOdinContext odinContext)
    {
        var db = _tenantSystemStorage.IdentityDatabase;

        var drive = await _driveManager.GetDriveIdByAlias(request.TargetDrive, db, false);

        if (null == drive)
        {
            await _driveManager.CreateDrive(request, odinContext, db);
            return true;
        }

        return false;
    }

    private async Task UpdateSystemCirclePermission(int key, bool shouldGrantKey, IOdinContext odinContext)
    {
        var systemCircle = _circleMembershipService.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, odinContext);

        if (shouldGrantKey)
        {
            if (!systemCircle.Permissions.Keys.Contains(key))
            {
                systemCircle.Permissions.Keys.Add(key);
            }
        }
        else
        {
            if (systemCircle.Permissions.Keys.Contains(key))
            {
                systemCircle.Permissions.Keys.Remove(key);
            }
        }

        await _dbs.UpdateCircleDefinition(systemCircle, odinContext);
    }
}
