﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;


namespace Odin.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;

        private readonly byte[] _appRegistrationDataType = Guid.Parse("14c83583-acfd-4368-89ad-6566636ace3d").ToByteArray();
        private readonly ThreeKeyValueStorage _appRegistrationValueStorage;

        private readonly byte[] _appClientDataType = Guid.Parse("54e60e2f-4687-449c-83ad-6ae6ff4ba1cf").ToByteArray();
        private readonly ThreeKeyValueStorage _appClientValueStorage;

        private readonly OdinContextCache _cache;
        private readonly TenantContext _tenantContext;

        private readonly IMediator _mediator;

        public AppRegistrationService(TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext, IMediator mediator, IcrKeyService icrKeyService)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _mediator = mediator;
            _icrKeyService = icrKeyService;

            const string appRegContextKey = "661e097f-6aa5-459f-a445-a9ea65348fde";
            _appRegistrationValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(appRegContextKey));

            const string appClientContextKey = "fb080b07-0566-4db8-bc0d-daed6b50b104";
            _appClientValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(appClientContextKey));

            _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        public async Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var hasTransit = this.HasRequestedTransit(request.PermissionSet);
            var icrKey = hasTransit ? _icrKeyService.GetDecryptedIcrKey(odinContext, cn) : null;

            var drives = new List<DriveGrantRequest>(request.Drives ?? new List<DriveGrantRequest>());

            if (hasTransit)
            {
                //Apps must be able to access the transient drive to send files directly over transit
                drives.Add(new DriveGrantRequest()
                {
                    PermissionedDrive = new()
                    {
                        Drive = SystemDriveConstants.TransientTempDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                });
            }

            var appGrant = await _exchangeGrantService.CreateExchangeGrant(cn, keyStoreKey, request.PermissionSet!, drives, masterKey, icrKey);

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

            _appRegistrationValueStorage.Upsert(cn, appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            await NotifyAppChanged(null, appReg, odinContext, cn);
            return appReg.Redacted();
        }

        public async Task UpdateAppPermissions(UpdateAppPermissionsRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(request.AppId, cn);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            //TODO: Should we regen the key store key?  

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = appReg.Grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var hasTransit = this.HasRequestedTransit(request.PermissionSet);
            var icrKey = hasTransit ? _icrKeyService.GetDecryptedIcrKey(odinContext, cn) : null;

            var drives = new List<DriveGrantRequest>(request.Drives ?? new List<DriveGrantRequest>());

            if (hasTransit)
            {
                //Apps must be able to access the transient drive to send files directly over transit
                drives.Add(new DriveGrantRequest()
                {
                    PermissionedDrive = new()
                    {
                        Drive = SystemDriveConstants.TransientTempDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                });
            }

            appReg.Grant = await _exchangeGrantService.CreateExchangeGrant(cn, keyStoreKey, request.PermissionSet!, drives, masterKey, icrKey);

            _appRegistrationValueStorage.Upsert(cn, request.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task UpdateAuthorizedCircles(UpdateAuthorizedCirclesRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var oldRegistration = await this.GetAppRegistrationInternal(request.AppId, cn);
            if (null == oldRegistration)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
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

            _appRegistrationValueStorage.Upsert(cn,request.AppId, GuidId.Empty, _appRegistrationDataType, updatedAppReg);

            //TODO: consider optimize by checking if anything actually changed before calling notify app changed

            await NotifyAppChanged(oldRegistration, updatedAppReg, odinContext, cn);
            ResetAppPermissionContextCache();
        }

        public async Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPk(GuidId appId, byte[] clientPublicKey,
            string friendlyName, IOdinContext odinContext, DatabaseConnection cn)
        {
            var (cat, corsHostName) = await this.RegisterClient(appId, friendlyName, odinContext, cn);

            var data = cat.ToPortableBytes();
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
            var encryptedData = publicKey.Encrypt(data);

            data.WriteZeros();

            var response = new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = encryptedData
            };

            return (response, corsHostName);
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(GuidId appId, string friendlyName, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId, cn);
            if (appReg == null)
            {
                throw new OdinClientException("App must be registered to add a client", OdinClientErrorCode.AppNotRegistered);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, masterKey, ClientTokenType.Other);

            var appClient = new AppClient(appId, friendlyName, accessRegistration);
            this.SaveClient(appClient, cn);
            return (cat, appReg.CorsHostName);
        }

        public async Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext, DatabaseConnection cn)
        {
            var result = await GetAppRegistrationInternal(appId, cn);
            return result?.Redacted();
        }

        public async Task<IOdinContext?> GetAppPermissionContext(ClientAuthenticationToken token, IOdinContext odinContext, DatabaseConnection cn)
        {
            async Task<IOdinContext> Creator()
            {
                var (isValid, accessReg, appReg) = await ValidateClientAuthToken(token, odinContext, cn);

                if (!isValid || null == appReg || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                if (!string.IsNullOrEmpty(appReg.CorsHostName))
                {
                    //just in case something changed in the db record
                    AppUtil.AssertValidCorsHeader(appReg.CorsHostName);
                }

                var grantDictionary = new Dictionary<Guid, ExchangeGrant> { { ByteArrayUtil.ReduceSHA256Hash("app_exchange_grant"), appReg.Grant } };

                //Note: isOwner = true because we passed ValidateClientAuthToken for an ap token above 
                var permissionContext =
                    await _exchangeGrantService.CreatePermissionContext(token, grantDictionary, accessReg, odinContext, cn, includeAnonymousDrives: true);

                var dotYouContext = new OdinContext()
                {
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

            var result = await _cache.GetOrAddContext(token, Creator);
            return result;
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(
            ClientAuthenticationToken authToken, IOdinContext odinContext, DatabaseConnection cn)
        {
            var appClient = _appClientValueStorage.Get<AppClient>(cn, authToken.Id);
            if (null == appClient)
            {
                return (false, null, null);
            }

            var appReg = await this.GetAppRegistrationInternal(appClient.AppId, cn);

            if (null == appReg || null == appReg.Grant)
            {
                return (false, null, null);
            }

            if (appClient.AccessRegistration.IsRevoked || appReg.Grant.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, appClient.AccessRegistration, appReg);
        }

        public async Task RevokeApp(GuidId appId, IOdinContext odinContext, DatabaseConnection cn)
        {
            var appReg = await this.GetAppRegistrationInternal(appId, cn);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }

            //TODO: revoke all clients? or is the one flag enough?
            _appRegistrationValueStorage.Upsert(cn, appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task RemoveAppRevocation(GuidId appId, IOdinContext odinContext, DatabaseConnection cn)
        {
            var appReg = await this.GetAppRegistrationInternal(appId, cn);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            _appRegistrationValueStorage.Upsert(cn, appId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients(GuidId appId, IOdinContext odinContext, DatabaseConnection cn)
        {
            var list = _appClientValueStorage.GetByCategory<AppClient>(cn, _appClientDataType);
            var resp = list.Where(appClient => appClient.AppId == appId).Select(appClient => new RegisteredAppClientResponse()
            {
                AppId = appClient.AppId,
                AccessRegistrationId = appClient.AccessRegistration.Id,
                FriendlyName = appClient.FriendlyName,
                IsRevoked = appClient.AccessRegistration.IsRevoked,
                Created = appClient.AccessRegistration.Created,
                AccessRegistrationClientType = appClient.AccessRegistration.AccessRegistrationClientType
            }).ToList();

            return await Task.FromResult(resp);
        }

        public async Task RevokeClient(GuidId accessRegistrationId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();
            var client = _appClientValueStorage.Get<AppClient>(cn, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = true;
            SaveClient(client, cn);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        public async Task DeleteCurrentAppClient(IOdinContext odinContext, DatabaseConnection cn)
        {
            var context = odinContext;
            var accessRegistrationId = context.Caller.OdinClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete app client");
            }

            var client = _appClientValueStorage.Get<AppClient>(cn, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _appClientValueStorage.Delete(cn, accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task DeleteClient(GuidId accessRegistrationId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var client = _appClientValueStorage.Get<AppClient>(cn, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _appClientValueStorage.Delete(cn, accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task AllowClient(GuidId accessRegistrationId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var client = _appClientValueStorage.Get<AppClient>(cn,accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = false;
            SaveClient(client, cn);
            await Task.CompletedTask;
        }

        public async Task DeleteApp(GuidId appId, IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var app = await GetAppRegistrationInternal(appId, cn);

            if (null == app)
            {
                throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
            }

            _appRegistrationValueStorage.Delete(cn, appId);

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

            await Task.CompletedTask;
        }

        public async Task<List<RedactedAppRegistration>> GetRegisteredApps(IOdinContext odinContext, DatabaseConnection cn)
        {
            odinContext.Caller.AssertHasMasterKey();

            var apps = _appRegistrationValueStorage.GetByCategory<AppRegistration>(cn, _appRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return await Task.FromResult(redactedList);
        }

        private void SaveClient(AppClient appClient, DatabaseConnection cn)
        {
            _appClientValueStorage.Upsert(cn, appClient.AccessRegistration.Id, appClient.AppId, _appClientDataType, appClient);
        }

        private async Task<AppRegistration?> GetAppRegistrationInternal(GuidId appId, DatabaseConnection cn)
        {
            var appReg = _appRegistrationValueStorage.Get<AppRegistration>(cn, appId);
            return await Task.FromResult(appReg);
        }

        private async Task NotifyAppChanged(AppRegistration? oldAppRegistration, AppRegistration newAppRegistration, IOdinContext odinContext, DatabaseConnection cn)
        {
            await _mediator.Publish(new AppRegistrationChangedNotification
            {
                OldAppRegistration = oldAppRegistration,
                NewAppRegistration = newAppRegistration,
                OdinContext = odinContext,
                DatabaseConnection = cn
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