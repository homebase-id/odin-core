#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        Task<RedactedAppRegistration> RegisterAppAsync(AppRegistrationRequest request, IOdinContext odinContext);

        Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// The registered app, or <see cref="OdinClientErrorCode.AppNotRegistered"/> when no app has that
        /// id.  For callers that are about to write the id somewhere it cannot easily be taken back from.
        /// </summary>
        Task<RedactedAppRegistration> GetRegisteredAppOrThrowAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// The app addressed as <c>{appSlug}</c>; see
        /// <see cref="AppRegistrationService.GetAppRegistrationBySlugAsync"/>.
        /// </summary>
        Task<RedactedAppRegistration?> GetAppRegistrationBySlugAsync(OdinSlug appSlug, IOdinContext odinContext);

        /// <summary>
        /// Resolves the appId of the app client making the current call (via its access-registration id),
        /// or null when the caller is not an app client (e.g. the owner console, or a guest).
        /// </summary>
        Task<GuidId?> GetCallingAppIdAsync(IOdinContext odinContext);

        Task<IOdinContext?> GetAppPermissionContextAsync(ClientAuthenticationToken token, IOdinContext odinContext);

        /// <summary>
        /// Updates the permissions granted to the app
        /// </summary>
        Task UpdateAppPermissionsAsync(UpdateAppPermissionsRequest request, IOdinContext odinContext);

        /// <summary>
        /// Updates the authorized circles and the permissions granted to them
        /// </summary>
        /// <returns></returns>
        Task UpdateAuthorizedCirclesAsync(UpdateAuthorizedCirclesRequest request, IOdinContext odinContext);

        /// <summary>
        /// Gives an app the slug the app tree names, if it has a different one.  Migration only.
        /// </summary>
        Task<bool> ApplyTreeSlugAsync(Guid appId, OdinSlug appSlug, IOdinContext odinContext);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetRegisteredAppsAsync(IOdinContext odinContext);

        /// <summary>
        /// Gets all apps which grant the specified circle
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetAppsGrantingCircleAsync(Guid circleId, IOdinContext odinContext);

        /// <summary>
        /// Removes access for a given application across all devices
        /// </summary>
        Task RevokeAppAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        Task RemoveAppRevocationAsync(GuidId appId, IOdinContext odinContext);

        // Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPkAsync(GuidId appId,
        //     byte[] clientPublicKey,
        //     string friendlyName, IOdinContext odinContext);


        /// <summary>
        /// Registers an application to be used on a given device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="friendlyName"></param>
        /// <param name="odinContext"></param>
        /// <param name="zkAppKeyId">The id of the app's zero-knowledge key the device is being handed with this token</param>
        /// <returns></returns>
        Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(GuidId appId, string friendlyName, IOdinContext odinContext,
            string? zkAppKeyId = null);

        /// <summary>The id of the app's zero-knowledge key (<see cref="AppRegistration.ZkAppKeyId"/>); null when it has none.</summary>
        Task<string?> GetZkAppKeyIdAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// Every app's zero-knowledge keys, for the owner console to hand an app's key to a device and to backfill what
        /// is missing (docs/zero-knowledge-drive-api-plan.md, step 3).
        /// </summary>
        Task<List<ZkAppKeysResponse>> GetZkAppKeysAsync(IOdinContext odinContext);

        /// <summary>Saves what the browser locked for an app: its zero-knowledge key if it had none, and drive keys.</summary>
        Task SaveZkAppKeysAsync(ZkAppKeysRequest request, IOdinContext odinContext);

        /// <summary>
        /// Converting a drive that keeps its key: every app reading it gets its locked copy of the drive's key, and every
        /// app's grant on it loses the copy the server could open.
        /// </summary>
        Task SetZkAppKeysForConversionAsync(Guid driveId, IReadOnlyCollection<ZkAppKeysRequest> requests, IOdinContext odinContext);

        /// <summary>A zero-knowledge drive's key changed: every app's copy of the old one goes.</summary>
        Task RemoveZkDriveCopiesAsync(Guid driveId, IOdinContext odinContext);

        Task<List<RegisteredAppClientResponse>> GetRegisteredClientsAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// Revokes a client from using the app
        /// </summary>
        Task RevokeClientAsync(GuidId accessRegistrationId, IOdinContext odinContext);

        Task DeleteClientAsync(GuidId accessRegistrationId, IOdinContext odinContext);

        Task AllowClientAsync(GuidId accessRegistrationId, IOdinContext odinContext);

        /// <summary>
        /// Removes a drive from every app's own grant and from the grant its circles' members get, ahead of the
        /// drive being deleted (#1869).
        /// </summary>
        Task RemoveDriveFromAllAppsAsync(Guid driveId, IOdinContext odinContext);

        /// <summary>Removes circles from every app's authorized circles, ahead of the circles being deleted (#1870).</summary>
        Task RemoveCirclesFromAllAppsAsync(IReadOnlyCollection<Guid> circleIds, IOdinContext odinContext);

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        Task DeleteCurrentAppClientAsync(IOdinContext odinContext);
    }
}