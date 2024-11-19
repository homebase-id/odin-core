#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;


namespace Odin.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;
        private readonly ILogger<AppRegistrationService> _logger;
        private readonly TableKeyThreeValue _tblKeyThreeValue;

        private readonly byte[] _appRegistrationDataType = Guid.Parse("14c83583-acfd-4368-89ad-6566636ace3d").ToByteArray();
        private readonly ThreeKeyValueStorage _appRegistrationValueStorage;

        private readonly byte[] _appClientDataType = Guid.Parse("54e60e2f-4687-449c-83ad-6ae6ff4ba1cf").ToByteArray();
        private readonly ThreeKeyValueStorage _appClientValueStorage;

        private readonly OdinContextCache _cache;
        private readonly TenantContext _tenantContext;

        private readonly IMediator _mediator;

        public AppRegistrationService(TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext, IMediator mediator,
            IcrKeyService icrKeyService,
            ILogger<AppRegistrationService> logger,
            TableKeyThreeValue tblKeyThreeValue)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _mediator = mediator;
            _icrKeyService = icrKeyService;
            _logger = logger;
            _tblKeyThreeValue = tblKeyThreeValue;

            const string appRegContextKey = "661e097f-6aa5-459f-a445-a9ea65348fde";
            _appRegistrationValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(appRegContextKey));

            const string appClientContextKey = "fb080b07-0566-4db8-bc0d-daed6b50b104";
            _appClientValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(appClientContextKey));

            _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        public async Task<RedactedAppRegistration> RegisterAppAsync(AppRegistrationRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var hasTransit = this.HasRequestedTransit(request.PermissionSet);
            var icrKey = hasTransit ? await _icrKeyService.GetDecryptedIcrKeyAsync(odinContext) : null;

            var drives = new List<DriveGrantRequest>(request.Drives ?? new List<DriveGrantRequest>());

            if (hasTransit)
            {
                // ensure the transient temp drive is added, once
                // Apps must be able to access the transient drive to send files directly over transit
                if (drives.All(d => d.PermissionedDrive.Drive != SystemDriveConstants.TransientTempDrive))
                {
                    drives.Add(new DriveGrantRequest()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = SystemDriveConstants.TransientTempDrive,
                            Permission = DrivePermission.ReadWrite
                        }
                    });                    
                }
            }

            var appGrant = await _exchangeGrantService.CreateExchangeGrantAsync(
                keyStoreKey, request.PermissionSet!, drives, masterKey, icrKey);

            //TODO: add check to ensure app name is unique
            //TODO: add check if app is already registered

            var appReg = new AppRegistration()
            {
                AppId = request.AppId,
                Name = request.Name,
                Grant = appGrant,

                CorsHostName = request.CorsHostName,
                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            await _appRegistrationValueStorage.UpsertAsync(_tblKeyThreeValue, appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            await NotifyAppChanged(null, appReg, odinContext);
            return appReg.Redacted();
        }

        public async Task UpdateAppPermissionsAsync(UpdateAppPermissionsRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternalAsync(request.AppId);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            //TODO: Should we regen the key store key?  

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = appReg.Grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var hasTransit = this.HasRequestedTransit(request.PermissionSet);
            var icrKey = hasTransit ? await _icrKeyService.GetDecryptedIcrKeyAsync(odinContext) : null;

            var drives = new List<DriveGrantRequest>(request.Drives ?? new List<DriveGrantRequest>());

            if (hasTransit)
            {
                // ensure the transient temp drive is added, once
                // Apps must be able to access the transient drive to send files directly over transit
                if (drives.All(d => d.PermissionedDrive.Drive != SystemDriveConstants.TransientTempDrive))
                {
                    drives.Add(new DriveGrantRequest()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = SystemDriveConstants.TransientTempDrive,
                            Permission = DrivePermission.ReadWrite
                        }
                    });                    
                }
            }

            appReg.Grant =
                await _exchangeGrantService.CreateExchangeGrantAsync(keyStoreKey, request.PermissionSet!, drives, masterKey, icrKey);

            await _appRegistrationValueStorage.UpsertAsync(_tblKeyThreeValue, request.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task UpdateAuthorizedCirclesAsync(UpdateAuthorizedCirclesRequest request, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var oldRegistration = await this.GetAppRegistrationInternalAsync(request.AppId);
            if (null == oldRegistration)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            if (request.AppId == SystemAppConstants.ChatAppId)
            {
                foreach (var cid in SystemAppConstants.ChatAppRegistrationRequest.AuthorizedCircles)
                {
                    request.AuthorizedCircles.EnsureItem(cid);
                }
            }
            
            if (request.AppId == SystemAppConstants.MailAppId)
            {
                foreach (var cid in SystemAppConstants.MailAppRegistrationRequest.AuthorizedCircles)
                {
                    request.AuthorizedCircles.EnsureItem(cid);
                }
            }

            var updatedAppReg = new AppRegistration()
            {
                AppId = oldRegistration.AppId,
                Name = oldRegistration.Name,
                Grant = oldRegistration.Grant,
                CorsHostName = oldRegistration.CorsHostName,

                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            await _appRegistrationValueStorage.UpsertAsync(_tblKeyThreeValue, request.AppId, GuidId.Empty, _appRegistrationDataType, updatedAppReg);

            //TODO: consider optimize by checking if anything actually changed before calling notify app changed

            await NotifyAppChanged(oldRegistration, updatedAppReg, odinContext);
            ResetAppPermissionContextCache();
        }

        public async Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPkAsync(GuidId appId,
            byte[] clientPublicKey,
            string friendlyName, IOdinContext odinContext)
        {
            var (cat, corsHostName) = await RegisterClientAsync(appId, friendlyName, odinContext);

            var data = cat.ToPortableBytes();
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
            var encryptedData = publicKey.Encrypt(data);

            data.Wipe();

            var response = new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = encryptedData
            };

            return (response, corsHostName);
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(GuidId appId, string friendlyName,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternalAsync(appId);
            if (appReg == null)
            {
                throw new OdinClientException("App must be registered to add a client", OdinClientErrorCode.AppNotRegistered);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var (accessRegistration, cat) =
                await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, masterKey, ClientTokenType.Other);

            var appClient = new AppClient(appId, friendlyName, accessRegistration);
            await SaveClientAsync(appClient);
            return (cat, appReg.CorsHostName);
        }

        public async Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext)
        {
            var result = await GetAppRegistrationInternalAsync(appId);
            return result?.Redacted();
        }

        public async Task<IOdinContext?> GetAppPermissionContextAsync(ClientAuthenticationToken token, IOdinContext odinContext)
        {
            async Task<IOdinContext> Creator()
            {
                var (isValid, accessReg, appReg) = await ValidateClientAuthTokenAsync(token, odinContext);

                if (!isValid || null == appReg || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                if (!string.IsNullOrEmpty(appReg.CorsHostName))
                {
                    //just in case something changed in the db record
                    AppUtil.AssertValidCorsHeader(appReg.CorsHostName);
                }

                var grantDictionary = new Dictionary<Guid, ExchangeGrant>
                {
                    { ByteArrayUtil.ReduceSHA256Hash("app_exchange_grant"), appReg.Grant }
                };
                
                //Note: isOwner = true because we passed ValidateClientAuthToken for an ap token above 
                var permissionContext = await _exchangeGrantService.CreatePermissionContext(
                    token,
                    grantDictionary,
                    accessReg,
                    odinContext,
                    includeAnonymousDrives: true);

                var dotYouContext = new OdinContext()
                {
                    Tenant = _tenantContext.HostOdinId,
                    Caller = new CallerContext(
                        odinId: _tenantContext.HostOdinId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Owner,
                        odinClientContext: new OdinClientContext()
                        {
                            ClientIdOrDomain = appReg.Name,
                            CorsHostName = appReg.CorsHostName,
                            AccessRegistrationId = accessReg.Id,
                            DevicePushNotificationKey = null
                        })
                };


                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            }

            var result = await _cache.GetOrAddContextAsync(token, Creator);
            return result;
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthTokenAsync(
            ClientAuthenticationToken authToken, IOdinContext odinContext)
        {

            var appClient = await _appClientValueStorage.GetAsync<AppClient>(_tblKeyThreeValue, authToken.Id);
            if (null == appClient)
            {
                _logger.LogDebug("null app client");
                return (false, null, null);
            }

            var appReg = await this.GetAppRegistrationInternalAsync(appClient.AppId);

            if (null == appReg || null == appReg.Grant)
            {
                _logger.LogDebug("null app registration or app registration grant");
                return (false, null, null);
            }

            if (appClient.AccessRegistration.IsRevoked || appReg.Grant.IsRevoked)
            {
                _logger.LogDebug("app client or app is revoked");
                return (false, null, null);
            }

            return (true, appClient.AccessRegistration, appReg);
        }

        public async Task<List<RedactedAppRegistration>> GetAppsGrantingCircleAsync(Guid circleId, IOdinContext odinContext)
        {
            var allApps = await GetRegisteredAppsInternalAsync();
            return allApps.Where(reg => reg.AuthorizedCircles?.Any(c => c == circleId) ?? false).ToList();
        }

        public async Task RevokeAppAsync(GuidId appId, IOdinContext odinContext)
        {
            var appReg = await this.GetAppRegistrationInternalAsync(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }

            //TODO: revoke all clients? or is the one flag enough?
            
            await _appRegistrationValueStorage.UpsertAsync(_tblKeyThreeValue, appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task RemoveAppRevocationAsync(GuidId appId, IOdinContext odinContext)
        {
            

            var appReg = await this.GetAppRegistrationInternalAsync(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            await _appRegistrationValueStorage.UpsertAsync(_tblKeyThreeValue, appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClientsAsync(GuidId appId, IOdinContext odinContext)
        {
            

            var list = await _appClientValueStorage.GetByCategoryAsync<AppClient>(_tblKeyThreeValue, _appClientDataType);
            var resp = list.Where(appClient => appClient.AppId == appId).Select(appClient => new RegisteredAppClientResponse()
            {
                AppId = appClient.AppId,
                AccessRegistrationId = appClient.AccessRegistration.Id,
                FriendlyName = appClient.FriendlyName,
                IsRevoked = appClient.AccessRegistration.IsRevoked,
                Created = appClient.AccessRegistration.Created,
                AccessRegistrationClientType = appClient.AccessRegistration.AccessRegistrationClientType
            }).ToList();

            return resp;
        }

        public async Task RevokeClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            

            odinContext.Caller.AssertHasMasterKey();
            var client = await _appClientValueStorage.GetAsync<AppClient>(_tblKeyThreeValue, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = true;
            await SaveClientAsync(client);
        }

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        public async Task DeleteCurrentAppClientAsync(IOdinContext odinContext)
        {
            

            var context = odinContext;
            var accessRegistrationId = context.Caller.OdinClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete app client");
            }

            var client = await _appClientValueStorage.GetAsync<AppClient>(_tblKeyThreeValue, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await _appClientValueStorage.DeleteAsync(_tblKeyThreeValue, accessRegistrationId);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            

            odinContext.Caller.AssertHasMasterKey();

            var client = await _appClientValueStorage.GetAsync<AppClient>(_tblKeyThreeValue, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await _appClientValueStorage.DeleteAsync(_tblKeyThreeValue, accessRegistrationId);
        }

        public async Task AllowClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            

            odinContext.Caller.AssertHasMasterKey();

            var client = await _appClientValueStorage.GetAsync<AppClient>(_tblKeyThreeValue, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = false;
            await SaveClientAsync(client);
        }

        public async Task DeleteAppAsync(GuidId appId, IOdinContext odinContext)
        {
            

            odinContext.Caller.AssertHasMasterKey();

            var app = await GetAppRegistrationInternalAsync(appId);

            if (null == app)
            {
                throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
            }

            await _appRegistrationValueStorage.DeleteAsync(_tblKeyThreeValue, appId);

            //TODO: reenable this after youauth domain work

            //
            // var clientsByApp = _appClientValueStorage.GetByKey2<AppClient>(appId);
            // using (_tenantSystemStorage.CreateCommitUnitOfWork())
            // {
            //     foreach (var c in clientsByApp)
            //     {
            //         _appClientValueStorage.Delete(c.AccessRegistration.Id);
            //     }
            //
            //     _appRegistrationValueStorage.Delete(appId);
            // }
        }

        public async Task<List<RedactedAppRegistration>> GetRegisteredAppsAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            return await GetRegisteredAppsInternalAsync();
        }

        private async Task<List<RedactedAppRegistration>> GetRegisteredAppsInternalAsync()
        {
            
            var apps = await _appRegistrationValueStorage.GetByCategoryAsync<AppRegistration>(_tblKeyThreeValue, _appRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return redactedList;
        }

        private async Task SaveClientAsync(AppClient appClient)
        {
            
            await _appClientValueStorage.UpsertAsync(_tblKeyThreeValue, appClient.AccessRegistration.Id, appClient.AppId, _appClientDataType, appClient);
        }

        private async Task<AppRegistration?> GetAppRegistrationInternalAsync(GuidId appId)
        {
            
            var appReg = await _appRegistrationValueStorage.GetAsync<AppRegistration>(_tblKeyThreeValue, appId);
            return appReg;
        }

        private async Task NotifyAppChanged(AppRegistration? oldAppRegistration, AppRegistration newAppRegistration,
            IOdinContext odinContext)
        {
            
            await _mediator.Publish(new AppRegistrationChangedNotification
            {
                OldAppRegistration = oldAppRegistration,
                NewAppRegistration = newAppRegistration,
                OdinContext = odinContext,
            });
        }

        /// <summary>
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        private void ResetAppPermissionContextCache()
        {
            _cache.Reset();
        }

        private bool HasRequestedTransit(PermissionSet? permissionSet)
        {
            if (null == permissionSet)
            {
                return false;
            }

            return permissionSet.HasKey(PermissionKeys.UseTransitRead) ||
                   permissionSet.HasKey(PermissionKeys.UseTransitWrite);
        }
    }
}