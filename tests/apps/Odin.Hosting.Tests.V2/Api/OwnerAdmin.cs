#nullable enable
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// V1 admin operations (drives, apps, circles, YouAuth domains) routed over the in-process pipeline
/// as the logged-in owner. V2 doesn't yet expose admin endpoints for these, so test setup uses V1
/// here even though the SUT calls in test bodies stay V2.
/// </summary>
/// <remarks>
/// Split across partial-class files by concern: this file holds the constructor + tenant init +
/// drives + circles; <c>OwnerAdmin.Apps.cs</c> covers app + app-client registration;
/// <c>OwnerAdmin.YouAuth.cs</c> covers YouAuth domains + clients. Every helper throws on non-2xx
/// via <see cref="EnsureSuccess{T}"/> — test setup that fails is always a broken test, never an
/// expected outcome.
/// </remarks>
public sealed partial class OwnerAdmin
{
    private readonly OwnerSession _owner;
    private readonly UniversalCircleNetworkApiClient _network;

    internal OwnerAdmin(OwnerSession owner)
    {
        _owner = owner;
        _network = new UniversalCircleNetworkApiClient(owner.Identity, owner.Factory);
    }

    // -----------------------------------------------------------------------------------------
    // Tenant initialization (one-time per identity — creates system circles + system drives)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the tenant initial-setup flow (system circles + system drives + any optional extras).
    /// Required for the peer connection-request flow to grant the <c>ConfirmedConnections</c>
    /// system circle. Idempotent on the server, so safe to call from per-fixture warm-up.
    /// </summary>
    public async Task<ApiResponse<bool>> InitializeIdentity(InitialSetupRequest? request = null)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ss);
        var response = await svc.InitializeIdentity(request ?? new InitialSetupRequest());
        EnsureSuccess(response, nameof(InitializeIdentity));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Drives
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a drive on the owner's identity. Defaults match the V2-test convention of
    /// anonymous-readable, non-owner-only, no subscriptions; callers typically pass these through
    /// from <see cref="DriveSpec"/>.
    /// </summary>
    public async Task<ApiResponse<bool>> CreateDrive(
        TargetDrive drive,
        string name,
        bool allowAnonymousReads = true,
        bool ownerOnly = false,
        bool allowSubscriptions = false,
        System.Collections.Generic.Dictionary<string, string>? attributes = null)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<IRefitDriveManagement>(client, ss);
        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = name,
            Metadata = string.Empty,
            AllowAnonymousReads = allowAnonymousReads,
            AllowSubscriptions = allowSubscriptions,
            OwnerOnly = ownerOnly,
            Attributes = attributes,
        });
        EnsureSuccess(response, nameof(CreateDrive));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Tenant configuration flags
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Toggles a tenant-scoped config flag (e.g. <c>DisableAutoAcceptConnectionRequests</c>).
    /// Tests flip these to put the recipient in a non-default acceptance mode and restore them
    /// in a <c>finally</c>.
    /// </summary>
    public async Task<ApiResponse<bool>> UpdateTenantSettingsFlag(
        Odin.Services.Configuration.TenantConfigFlagNames flag, string value)
    {
        var (client, ss) = _owner.NewAdminHttpClient();
        var svc = RefitCreator.RestServiceFor<Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration.IRefitOwnerConfiguration>(client, ss);
        var response = await svc.UpdateSystemConfigFlag(
            new Odin.Services.Configuration.UpdateFlagRequest
            {
                FlagName = System.Enum.GetName(flag),
                Value = value
            });
        EnsureSuccess(response, nameof(UpdateTenantSettingsFlag));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Circles (delegated to the existing new-style client; works with our factory unchanged)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a circle that members will be granted on connection. Used by <see cref="GuestSession"/>
    /// to attach a YouAuth domain to a drive-permission grant.
    /// </summary>
    public async Task<ApiResponse<HttpContent>> CreateCircle(Guid id, string name, PermissionSetGrantRequest grant)
    {
        var response = await _network.CreateCircle(id, name, grant);
        EnsureSuccess(response, nameof(CreateCircle));
        return response;
    }

    // -----------------------------------------------------------------------------------------
    // Shared throw-on-non-2xx helper used by every public method in the partial-class set.
    // -----------------------------------------------------------------------------------------

    private static void EnsureSuccess<T>(ApiResponse<T> response, string opName)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{opName} failed: {(int)response.StatusCode} {response.StatusCode}" +
                (response.Error?.Content is { Length: > 0 } body ? $" — {body}" : ""));
        }
    }
}
