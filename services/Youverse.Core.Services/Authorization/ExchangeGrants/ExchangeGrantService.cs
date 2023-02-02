﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Creates and manages grants the access of exchanging data.
    /// </summary>
    public class ExchangeGrantService
    {
        private readonly ILogger<ExchangeGrantService> _logger;
        private readonly IDriveService _driveService;
        private readonly CircleDefinitionService _circleDefinitionService;

        public ExchangeGrantService(ILogger<ExchangeGrantService> logger, IDriveService driveService, CircleDefinitionService circleDefinitionService)
        {
            _logger = logger;
            _driveService = driveService;
            _circleDefinitionService = circleDefinitionService;
        }

        /// <summary>
        /// Creates an <see cref="ExchangeGrant"/> using the specified key store key and request
        /// </summary>
        public async Task<ExchangeGrant> CreateExchangeGrant(SensitiveByteArray grantKeyStoreKey, PermissionSetGrantRequest request, SensitiveByteArray masterKey)
        {
            return await this.CreateExchangeGrant(grantKeyStoreKey, permissionSet: request.PermissionSet, driveGrantRequests: request.Drives, masterKey);
        }

        /// <summary>
        /// Creates an <see cref="ExchangeGrant"/> using the specified key store key
        /// </summary>
        public async Task<ExchangeGrant> CreateExchangeGrant(SensitiveByteArray grantKeyStoreKey, PermissionSet permissionSet, IEnumerable<DriveGrantRequest> driveGrantRequests,
            SensitiveByteArray masterKey)
        {
            var driveGrants = new List<DriveGrant>();

            if (driveGrantRequests != null)
            {
                foreach (var req in driveGrantRequests)
                {
                    //Note: fail the whole operation (CreateExchangeGrant) if an invalid drive is specified (the true flag will ensure we throw an exception)
                    var driveId = await _driveService.GetDriveIdByAlias(req.PermissionedDrive.Drive, true);
                    var drive = await _driveService.GetDrive(driveId.GetValueOrDefault(), true);

                    var driveGrant = CreateDriveGrant(drive, req.PermissionedDrive.Permission, grantKeyStoreKey, masterKey);
                    driveGrants.Add(driveGrant);
                }
            }

            var grant = new ExchangeGrant()
            {
                Created = UnixTimeUtc.Now().milliseconds,
                MasterKeyEncryptedKeyStoreKey = masterKey == null ? null : new SymmetricKeyEncryptedAes(ref masterKey, ref grantKeyStoreKey),
                IsRevoked = false,
                KeyStoreKeyEncryptedDriveGrants = driveGrants.ToList(),
                PermissionSet = permissionSet
            };

            return grant;
        }

        /// <summary>
        /// Creates an <see cref="ExchangeGrant"/> using a generated key store key
        /// </summary>
        /// <param name="permissionSet"></param>
        /// <param name="driveGrantRequests"></param>
        /// <param name="masterKey"></param>
        /// <returns></returns>
        public async Task<ExchangeGrant> CreateExchangeGrant(PermissionSet permissionSet, IEnumerable<DriveGrantRequest> driveGrantRequests, SensitiveByteArray? masterKey)
        {
            var grantKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var grant = await this.CreateExchangeGrant(grantKeyStoreKey, permissionSet, driveGrantRequests, masterKey);
            grantKeyStoreKey.Wipe();

            return grant;
        }

        /// <summary>
        /// Creates a new client access token for the specified grant.
        /// </summary>
        /// <param name="grant"></param>
        /// <param name="masterKey"></param>
        /// <param name="tokenType"></param>
        /// <returns></returns>
        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(ExchangeGrant grant, SensitiveByteArray? masterKey, ClientTokenType tokenType)
        {
            if (grant.IsRevoked)
            {
                throw new YouverseSecurityException("Cannot create Client Access Token for a revoked ExchangeGrant");
            }

            SensitiveByteArray grantKeyStoreKey = null;

            if (masterKey != null)
            {
                grantKeyStoreKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            }

            var token = await this.CreateClientAccessToken(grantKeyStoreKey, tokenType);
            grantKeyStoreKey?.Wipe();
            return token;
        }

        public async Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessToken(SensitiveByteArray grantKeyStoreKey, ClientTokenType tokenType)
        {
            var (accessReg, clientAccessToken) = await this.CreateClientAccessTokenInternal(grantKeyStoreKey, tokenType);
            return (accessReg, clientAccessToken);
        }
        
        public async Task<PermissionContext> CreatePermissionContext(ClientAuthenticationToken authToken, Dictionary<string, ExchangeGrant> grants, AccessRegistration accessReg,
            List<int>? additionalPermissionKeys = null, bool includeAnonymousDrives = false)
        {
            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var (grantKeyStoreKey, sharedSecret) = accessReg.DecryptUsingClientAuthenticationToken(authToken);

            var permissionGroupMap = new Dictionary<string, PermissionGroup>();
            if (grants != null)
            {
                foreach (var key in grants.Keys)
                {
                    var exchangeGrant = grants[key];
                    var pg = new PermissionGroup(exchangeGrant.PermissionSet, exchangeGrant.KeyStoreKeyEncryptedDriveGrants, grantKeyStoreKey);
                    permissionGroupMap.Add(key, pg);

                    foreach (var x in exchangeGrant.KeyStoreKeyEncryptedDriveGrants)
                    {
                        _logger.LogInformation(
                            $"Auth Token with Id: [{authToken.Id}] Access granted to [{x.DriveId}] (alias:{x.PermissionedDrive.Drive.Alias.ToBase64()} | type: {x.PermissionedDrive.Drive.Type.ToBase64()})");
                    }
                }
            }

            if (includeAnonymousDrives)
            {
                //MergeAnonymousDrives
                //TODO: remove any anonymous drives which are explicitly granted above
                permissionGroupMap.Add("anonymous_drives", await this.CreateAnonymousDrivePermissionGroup());
            }

            if (additionalPermissionKeys != null)
            {
                permissionGroupMap.Add("additional_permissions", new PermissionGroup(new PermissionSet(additionalPermissionKeys), null, null));
            }

            var permissionCtx = new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: sharedSecret
            );

            return permissionCtx;
        }


        /// <summary>
        /// Creates a permission group of anonymous drives
        /// </summary>
        /// <param name="permissionKeys">The permission keys to add to the permission group</param>
        /// <returns></returns>
        public async Task<PermissionGroup> CreateAnonymousDrivePermissionGroup()
        {
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);
            var anonDriveGrants = anonymousDrives.Results.Select(drive => this.CreateDriveGrant(drive, DrivePermission.Read, null, null));
            return new PermissionGroup(new PermissionSet(), anonDriveGrants, null);
        }
        
        //
        
        private DriveGrant CreateDriveGrant(StorageDrive drive, DrivePermission permission, SensitiveByteArray grantKeyStoreKey, SensitiveByteArray masterKey)
        {
            var storageKey = masterKey == null ? null : drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);

            var dk = new DriveGrant()
            {
                DriveId = drive.Id,
                KeyStoreKeyEncryptedStorageKey = (storageKey == null || grantKeyStoreKey == null) ? null : new SymmetricKeyEncryptedAes(ref grantKeyStoreKey, ref storageKey),
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = drive.TargetDriveInfo,
                    Permission = permission
                }
            };

            storageKey?.Wipe();

            return dk;
        }

        private Task<(AccessRegistration, ClientAccessToken)> CreateClientAccessTokenInternal(SensitiveByteArray grantKeyStoreKey, ClientTokenType tokenType,
            AccessRegistrationClientType clientType = AccessRegistrationClientType.Other)
        {
            var accessKeyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var serverAccessKey = new SymmetricKeyEncryptedXor(ref accessKeyStoreKey, out var clientAccessKey);

            var sharedSecret = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

            var reg = new AccessRegistration()
            {
                Id = GuidId.NewId(),
                AccessRegistrationClientType = clientType,
                Created = UnixTimeUtc.Now().milliseconds,
                ClientAccessKeyEncryptedKeyStoreKey = serverAccessKey,
                AccessKeyStoreKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(secret: ref accessKeyStoreKey, dataToEncrypt: ref sharedSecret),
                IsRevoked = false,
                AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey = grantKeyStoreKey == null ? null : new SymmetricKeyEncryptedAes(secret: ref accessKeyStoreKey, dataToEncrypt: ref grantKeyStoreKey)
            };

            accessKeyStoreKey.Wipe();

            //Note: we have to send both the id and the AccessTokenHalfKey back to the server
            var cat = new ClientAccessToken()
            {
                Id = reg.Id,
                AccessTokenHalfKey = clientAccessKey,
                SharedSecret = sharedSecret,
                ClientTokenType = tokenType
            };

            return Task.FromResult((reg, cat));
        }

        private async Task<IEnumerable<DriveGrant>> MergeAnonymousDrives(IEnumerable<DriveGrant> driveGrants)
        {
            var list = driveGrants?.ToList();
            Guard.Argument(list, nameof(driveGrants)).NotNull();

            //get the anonymous drives
            //merge; existing drive grants take priority because they likely have keys for decryption
            var finalGrantList = new List<DriveGrant>(list!);
            var anonymousDrives = await _driveService.GetAnonymousDrives(PageOptions.All);

            foreach (var drive in anonymousDrives.Results)
            {
                //add new ones
                if (list.All(g => g.DriveId != drive.Id))
                {
                    var grant = CreateDriveGrant(drive, DrivePermission.Read, null, null);
                    finalGrantList.Add(grant);
                }
            }

            return finalGrantList;
        }
    }
}