#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Services.Apps.V2;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Util;

namespace Odin.Services.Authorization.BundleTokens;

/// <summary>
/// One consent for several apps: installs or updates every app in the request that came with a manifest,
/// then issues one bundle token for all of them.
/// </summary>
/// <remarks>
/// The whole request is validated before anything is written -- every manifest, plus conflicts between
/// manifests (two apps claiming one slug, drive or circle) that no single manifest's validation can see.
/// After that the apps are applied one at a time; each application is retry-safe, so a failure part-way
/// is recovered by authorizing again.
/// </remarks>
public class BundleAuthorizationService(
    AppRegistrationV2Service appRegistrationV2Service,
    IAppRegistrationService appRegistrationService,
    BundleTokenService bundleTokenService)
{
    public async Task<BundleAuthorizationPreview> PreviewAsync(BundleAuthorizationRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        var problems = new List<AppRegistrationProblem>();
        void Problem(string code, string subject, string message) =>
            problems.Add(new AppRegistrationProblem { Code = code, Subject = subject, Message = message });

        var apps = request.Apps ?? [];
        if (apps.Count == 0)
        {
            Problem("noApps", "apps", "The request names no apps");
        }

        if (apps.Count > BundleTokenService.MaxAppsPerToken)
        {
            Problem("tooManyApps", "apps", $"A token can reach at most {BundleTokenService.MaxAppsPerToken} apps");
        }

        if (string.IsNullOrWhiteSpace(request.FriendlyName))
        {
            Problem("friendlyNameRequired", "friendlyName", "A name for this client is required");
        }

        if (apps.All(a => a?.AppId != request.PrimaryAppId))
        {
            Problem("primaryAppMissing", "primaryAppId", "The primary app must be one of the apps in the request");
        }

        var previews = new List<BundleAppPreview>();
        var seenApps = new HashSet<Guid>();
        var slugOwners = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var driveOwners = new Dictionary<Guid, Guid>();
        var circleOwners = new Dictionary<Guid, Guid>();

        for (var i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            var appProblems = new List<AppRegistrationProblem>();
            void AppProblem(string code, string message) =>
                appProblems.Add(new AppRegistrationProblem { Code = code, Subject = $"apps[{i}]", Message = message });

            if (app == null || app.AppId == Guid.Empty)
            {
                Problem("appIdRequired", $"apps[{i}]", "An app id is required");
                continue;
            }

            if (!seenApps.Add(app.AppId))
            {
                Problem("duplicateApp", $"apps[{i}]", $"App {app.AppId} is listed more than once");
                continue;
            }

            var manifest = app.Manifest;
            if (manifest != null && manifest.AppId == Guid.Empty)
            {
                manifest.AppId = app.AppId;
            }

            if (manifest != null && manifest.AppId != app.AppId)
            {
                AppProblem("manifestAppIdMismatch", "The manifest describes a different app");
                manifest = null;
            }

            var existing = await appRegistrationService.GetAppRegistration(app.AppId, odinContext);
            AppRegistrationValidationResult? validation = null;
            var action = BundleAppAction.None;

            if (manifest == null)
            {
                if (existing == null)
                {
                    AppProblem("appNotRegistered", "This app is not registered and the request did not include its manifest");
                }
            }
            else
            {
                validation = await appRegistrationV2Service.ValidateAsync(manifest, odinContext);
                action = !validation.IsRegistered ? BundleAppAction.Install
                    : HasChanges(validation.Diff) ? BundleAppAction.Update
                    : BundleAppAction.None;

                // Conflicts between manifests in the same request.
                if (!string.IsNullOrEmpty(manifest.AppSlug) && !slugOwners.TryAdd(manifest.AppSlug, app.AppId))
                {
                    AppProblem("slugTaken", $"Another app in this request also uses the slug '{manifest.AppSlug}'");
                }

                foreach (var d in manifest.OwnedDrives ?? [])
                {
                    if (d?.TargetDrive?.Alias != null && !driveOwners.TryAdd(d.TargetDrive.Alias, app.AppId))
                    {
                        AppProblem("driveOwnedElsewhere", $"Another app in this request also declares the drive '{d.Name}'");
                    }
                }

                foreach (var c in manifest.OwnedCircles ?? [])
                {
                    if (c != null && c.Id != Guid.Empty && !circleOwners.TryAdd(c.Id, app.AppId))
                    {
                        AppProblem("circleOwnedElsewhere", $"Another app in this request also declares the circle '{c.Name}'");
                    }
                }
            }

            if (existing?.IsRevoked ?? false)
            {
                AppProblem("appRevoked", "This app is revoked; allow it again before including it");
            }

            previews.Add(new BundleAppPreview
            {
                AppId = app.AppId,
                Name = existing?.Name ?? manifest?.Name ?? "",
                AppSlug = existing?.AppSlug ?? manifest?.AppSlug ?? "",
                IsPrimary = app.AppId == request.PrimaryAppId,
                IsRegistered = existing != null,
                IsReserved = AppRegistrationV2Service.IsReserved(app.AppId),
                IsRevoked = existing?.IsRevoked ?? false,
                HasManifest = manifest != null,
                Action = action,
                Validation = validation,
                Problems = appProblems
            });
        }

        var primary = apps.FirstOrDefault(a => a?.AppId == request.PrimaryAppId);
        if (primary != null)
        {
            var corsHostName = primary.Manifest?.CorsHostName ??
                               (await appRegistrationService.GetAppRegistration(primary.AppId, odinContext))?.CorsHostName;
            var redirectProblem = BundleTokenService.RedirectProblem(corsHostName, request.RedirectUri);
            if (redirectProblem != null)
            {
                Problem("redirectNotAllowed", "redirectUri", redirectProblem);
            }
        }

        return new BundleAuthorizationPreview
        {
            IsValid = problems.Count == 0 &&
                      previews.All(p => p.Problems.Count == 0 && (p.Validation?.IsValid ?? true)),
            Apps = previews,
            Problems = problems
        };
    }

    /// <summary>
    /// Installs or updates every app that came with a manifest, then issues one bundle token for all of
    /// the apps and seals it for the client.
    /// </summary>
    public async Task<BeginBundleTokenExchangeResponse> AuthorizeAsync(BundleAuthorizationRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        // Before anything is written: a bad key found after the apps are installed would leave them
        // installed with no token.
        try
        {
            EccPublicKeyData.FromJwkBase64UrlPublicKey(request.JwkBase64UrlPublicKey);
        }
        catch (Exception)
        {
            throw new OdinClientException("The public key is not a valid JWK", OdinClientErrorCode.ArgumentError);
        }

        var preview = await PreviewAsync(request, odinContext);
        if (!preview.IsValid)
        {
            var messages = preview.Problems
                .Concat(preview.Apps.SelectMany(a => a.Problems))
                .Concat(preview.Apps.SelectMany(a => (a.Validation?.Problems ?? []).Select(p => new AppRegistrationProblem
                {
                    Code = p.Code,
                    Subject = string.IsNullOrEmpty(a.Name) ? p.Subject : $"{a.Name}: {p.Subject}",
                    Message = p.Message
                })))
                .Select(p => string.IsNullOrEmpty(p.Subject) ? p.Message : $"{p.Subject}: {p.Message}");

            throw new OdinClientException(string.Join("; ", messages), OdinClientErrorCode.ArgumentError);
        }

        foreach (var app in request.Apps.Where(a => a.Manifest != null))
        {
            await appRegistrationV2Service.ApplyAsync(app.Manifest!, odinContext);
        }

        return await bundleTokenService.BeginExchangeAsync(new BeginBundleTokenExchangeRequest
        {
            PrimaryAppId = request.PrimaryAppId,
            AppIds = request.Apps.Select(a => a.AppId).ToList(),
            FriendlyName = request.FriendlyName,
            JwkBase64UrlPublicKey = request.JwkBase64UrlPublicKey,
            RedirectUri = request.RedirectUri
        }, odinContext);
    }

    private static bool HasChanges(AppRegistrationDiff diff)
    {
        return diff.DrivesToCreate.Count > 0 || diff.CirclesToCreate.Count > 0 ||
               diff.DriveAccessGained.Count > 0 || diff.DriveAccessLost.Count > 0 ||
               diff.PermissionKeysGained.Count > 0 || diff.PermissionKeysLost.Count > 0 ||
               diff.AuthorizedCirclesAdded.Count > 0 || diff.AuthorizedCirclesRemoved.Count > 0;
    }
}
