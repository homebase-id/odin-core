using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
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
    private readonly CircleNetworkService _cns;
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

    public TenantConfigService(CircleNetworkService cns,
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
        _cns = cns;
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

        using var cn = _tenantSystemStorage.CreateConnection();
        _tenantContext.UpdateSystemConfig(GetTenantSettings(cn));
    }

    public bool IsIdentityServerConfigured(DatabaseConnection cn)
    {
        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = _configStorage.Get<FirstRunInfo>(cn, FirstRunInfo.Key);
        return firstRunInfo != null;
    }

    public bool IsEulaSignatureRequired(IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        var info = _configStorage.Get<List<EulaSignature>>(cn, EulaSystemInfo.StorageKey);
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

    public List<EulaSignature> GetEulaSignatureHistory(IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        var signatures = _configStorage.Get<List<EulaSignature>>(cn, EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        return signatures;
    }

    public void MarkEulaSigned(MarkEulaSignedRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Version, nameof(request.Version));

        if (request.Version != EulaSystemInfo.RequiredVersion)
        {
            throw new OdinClientException("Invalid Eula version");
        }

        var signatures = _configStorage.Get<List<EulaSignature>>(cn, EulaSystemInfo.StorageKey) ?? new List<EulaSignature>();

        signatures.Add(new EulaSignature()
        {
            SignatureDate = UnixTimeUtc.Now(),
            Version = request.Version,
            SignatureBytes = request.SignatureBytes
        });

        _configStorage.Upsert(cn, Eula.EulaSystemInfo.StorageKey, signatures);
    }

    public async Task CreateInitialKeys(IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        await _recoverService.CreateInitialKey(odinContext, cn);

        await _publicPrivateKeyService.CreateInitialKeys(odinContext, cn);

        await _icrKeyService.CreateInitialKeys(odinContext, cn);
    }

    /// <summary>
    /// Configures aspects of the owner's identity that require the master key
    /// </summary>
    public async Task EnsureInitialOwnerSetup(InitialSetupRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (request.FirstRunToken.HasValue)
        {
            await _registry.MarkRegistrationComplete(request.FirstRunToken.GetValueOrDefault());
        }

        //Note: the order here is important.  if the request or system drives include any anonymous
        //drives, they should be added after the system circle exists
        await _circleMembershipService.CreateSystemCircles(odinContext, cn);

        await CreateDriveIfNotExists(SystemDriveConstants.CreateChatDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateMailDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateFeedDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateHomePageConfigDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreatePublicPostsChannelDriveRequest, odinContext, cn);

        await CreateDriveIfNotExists(SystemDriveConstants.CreateContactDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateProfileDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateWalletDriveRequest, odinContext, cn);
        await CreateDriveIfNotExists(SystemDriveConstants.CreateTransientTempDriveRequest, odinContext, cn);

        foreach (var rd in request.Drives ?? new List<CreateDriveRequest>())
        {
            await CreateDriveIfNotExists(rd, odinContext, cn);
        }

        //Create additional circles last in case they rely on any of the drives above
        foreach (var rc in request.Circles ?? new List<CreateCircleRequest>())
        {
            await CreateCircleIfNotExists(rc, odinContext, cn);
        }

        await this.RegisterBuiltInApps(odinContext, cn);

        cn.CreateCommitUnitOfWork(() =>
        {
            _configStorage.Upsert(cn, TenantSettings.ConfigKey, TenantSettings.Default);
            _configStorage.Upsert(cn, FirstRunInfo.Key, new FirstRunInfo()
            {
                FirstRunDate = UnixTimeUtc.Now().milliseconds
            });
        });
    }

    public async Task UpdateSystemFlag(UpdateFlagRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (!Enum.TryParse(typeof(TenantConfigFlagNames), request.FlagName, true, out var flag))
        {
            throw new OdinClientException("Invalid flag name", OdinClientErrorCode.InvalidFlagName);
        }

        var cfg = _configStorage.Get<TenantSettings>(cn, TenantSettings.ConfigKey) ?? new TenantSettings();

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
                await UpdateSystemCirclePermission(PermissionKeys.ReadWhoIFollow, cfg.AllConnectedIdentitiesCanViewWhoIFollow, odinContext, cn);
                break;

            case TenantConfigFlagNames.AnonymousVisitorsCanViewConnections:
                cfg.AnonymousVisitorsCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.AuthenticatedIdentitiesCanViewConnections:
                cfg.AuthenticatedIdentitiesCanViewConnections = bool.Parse(request.Value);
                break;

            case TenantConfigFlagNames.ConnectedIdentitiesCanViewConnections:
                cfg.AllConnectedIdentitiesCanViewConnections = bool.Parse(request.Value);
                await UpdateSystemCirclePermission(PermissionKeys.ReadConnections, cfg.AllConnectedIdentitiesCanViewConnections, odinContext, cn);
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

            case TenantConfigFlagNames.AutoAcceptIntroductions:
                cfg.AutoAcceptIntroductions = bool.Parse(request.Value);
                break;

            default:
                throw new OdinClientException("Flag name is valid but not handled",
                    OdinClientErrorCode.UnknownFlagName);
        }

        _configStorage.Upsert(cn, TenantSettings.ConfigKey, cfg);

        //TODO: eww, use mediator instead
        _tenantContext.UpdateSystemConfig(cfg);
    }

    public TenantSettings GetTenantSettings(DatabaseConnection cn)
    {
        return _configStorage.Get<TenantSettings>(cn, TenantSettings.ConfigKey) ?? TenantSettings.Default;
    }

    public OwnerAppSettings GetOwnerAppSettings(IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();
        return _configStorage.Get<OwnerAppSettings>(cn, OwnerAppSettings.ConfigKey) ?? OwnerAppSettings.Default;
    }

    public void UpdateOwnerAppSettings(OwnerAppSettings newSettings, IOdinContext odinContext, DatabaseConnection cn)
    {
        odinContext.Caller.AssertHasMasterKey();
        _configStorage.Upsert(cn, OwnerAppSettings.ConfigKey, newSettings);
    }

    //

    private async Task RegisterBuiltInApps(IOdinContext odinContext, DatabaseConnection cn)
    {
        await RegisterChatApp(odinContext, cn);
        await RegisterMailApp(odinContext, cn);
        await RegisterFeedApp(odinContext, cn);
        // await RegisterPhotosApp();
    }

    private async Task RegisterFeedApp(IOdinContext odinContext, DatabaseConnection cn)
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

        await _appRegistrationService.RegisterApp(request, odinContext, cn);
    }


    private async Task RegisterChatApp(IOdinContext odinContext, DatabaseConnection cn)
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.ChatAppId,
            Name = "Homebase - Chat",
            AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
            {
                SystemCircleConstants.ConnectedIdentitiesSystemCircleId,
                SystemCircleConstants.ConfirmedConnectionsCircleId,
                SystemCircleConstants.AutoConnectionsCircleId
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

        await _appRegistrationService.RegisterApp(request, odinContext, cn);
    }

    private async Task RegisterMailApp(IOdinContext odinContext, DatabaseConnection cn)
    {
        var request = new AppRegistrationRequest()
        {
            AppId = SystemAppConstants.MailAppId,
            Name = "Homebase - Mail",
            AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
            {
                SystemCircleConstants.ConnectedIdentitiesSystemCircleId,
                SystemCircleConstants.ConfirmedConnectionsCircleId,
                SystemCircleConstants.AutoConnectionsCircleId
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

        await _appRegistrationService.RegisterApp(request, odinContext, cn);
    }

    private async Task<bool> CreateCircleIfNotExists(CreateCircleRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var existingCircleDef = _circleMembershipService.GetCircle(request.Id, odinContext, cn);
        if (null == existingCircleDef)
        {
            await _circleMembershipService.CreateCircleDefinition(request, odinContext, cn);
            return true;
        }

        return false;
    }

    private async Task<bool> CreateDriveIfNotExists(CreateDriveRequest request, IOdinContext odinContext, DatabaseConnection cn)
    {
        var drive = await _driveManager.GetDriveIdByAlias(request.TargetDrive, cn, false);

        if (null == drive)
        {
            await _driveManager.CreateDrive(request, odinContext, cn);
            return true;
        }

        return false;
    }

    private async Task UpdateSystemCirclePermission(int key, bool shouldGrantKey, IOdinContext odinContext, DatabaseConnection cn)
    {
        var systemCircle = _circleMembershipService.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, odinContext, cn);

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

        await _cns.UpdateCircleDefinition(systemCircle, odinContext, cn);
    }
}
