#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Services.Apps.V2;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Util;
using Codes = Odin.Services.Apps.V2.AppRegistrationProblemCodes;

namespace Odin.Services.Authorization.BundleTokens;

/// <summary>
/// One consent for several apps: installs or updates every app in the request that came with a manifest,
/// then issues one bundle token for all of them.
/// </summary>
/// <remarks>
/// The manifests are planned together, against one snapshot, so an app may grant a drive another app in
/// the same request declares, and two apps claiming one slug, drive or circle is caught before anything is
/// written.  The validated plans are then applied as they are; each application is retry-safe, so a
/// failure part-way is recovered by authorizing again.
/// </remarks>
public class BundleAuthorizationService(
    AppRegistrationV2Service appRegistrationV2Service,
    IAppRegistrationService appRegistrationService,
    BundleTokenService bundleTokenService)
{
    public async Task<BundleAuthorizationPreview> PreviewAsync(BundleAuthorizationRequest request, IOdinContext odinContext)
    {
        return (await PreviewCoreAsync(request, odinContext)).preview;
    }

    /// <summary>
    /// Installs or updates every app that came with a manifest, then issues one bundle token for all of
    /// the apps and seals it for the client.
    /// </summary>
    public async Task<BeginBundleTokenExchangeResponse> AuthorizeAsync(BundleAuthorizationRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        // Before anything is written: a bad key found after the apps are installed would leave them
        // installed with no token.
        var publicKey = BundleTokenService.ParsePublicKey(request.JwkBase64UrlPublicKey);

        var (preview, plans) = await PreviewCoreAsync(request, odinContext);
        AppRegistrationProblem.ThrowIfAny(preview.Problems
            .Concat(preview.Apps.SelectMany(a => a.Problems))
            .Concat(preview.Apps.SelectMany(a => (a.Validation?.Problems ?? []).Select(p =>
                AppRegistrationProblem.Of(p.Code, string.IsNullOrEmpty(a.Name) ? p.Subject : $"{a.Name}: {p.Subject}", p.Message)))));

        await appRegistrationV2Service.ApplyPlansAsync(plans, odinContext);

        return await bundleTokenService.BeginExchangeAsync(new IssueBundleTokenRequest
        {
            PrimaryAppId = request.PrimaryAppId,
            AppIds = request.Apps.Select(a => a.AppId).ToList(),
            FriendlyName = request.FriendlyName,
            RedirectUri = request.RedirectUri
        }, publicKey, odinContext);
    }

    private async Task<(BundleAuthorizationPreview preview, List<AppRegistrationPlan> plans)> PreviewCoreAsync(
        BundleAuthorizationRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        var problems = new List<AppRegistrationProblem>();
        var apps = request.Apps ?? [];

        if (apps.Count == 0)
        {
            problems.Add(AppRegistrationProblem.Of(Codes.NoApps, "apps", "The request names no apps"));
        }

        if (apps.Count > BundleTokenService.MaxAppsPerToken)
        {
            problems.Add(AppRegistrationProblem.Of(Codes.TooManyApps, "apps", $"A token can reach at most {BundleTokenService.MaxAppsPerToken} apps"));
        }

        if (string.IsNullOrWhiteSpace(request.FriendlyName))
        {
            problems.Add(AppRegistrationProblem.Of(Codes.FriendlyNameRequired, "friendlyName", "A name for this client is required"));
        }

        if (apps.All(a => a?.AppId != request.PrimaryAppId))
        {
            problems.Add(AppRegistrationProblem.Of(Codes.PrimaryAppMissing, "primaryAppId", "The primary app must be one of the apps in the request"));
        }

        // Well-formed, distinct entries; a manifest always describes its own entry.
        var entries = new List<(int index, BundleAppRequest app, List<AppRegistrationProblem> problems)>();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            if (app == null || app.AppId == Guid.Empty)
            {
                problems.Add(AppRegistrationProblem.Of(Codes.AppIdRequired, $"apps[{i}]", "An app id is required"));
                continue;
            }

            if (!seen.Add(app.AppId))
            {
                problems.Add(AppRegistrationProblem.Of(Codes.DuplicateApp, $"apps[{i}]", $"App {app.AppId} is listed more than once"));
                continue;
            }

            var appProblems = new List<AppRegistrationProblem>();
            if (app.Manifest != null && app.Manifest.AppId == Guid.Empty)
            {
                app.Manifest.AppId = app.AppId;
            }

            if (app.Manifest != null && app.Manifest.AppId != app.AppId)
            {
                appProblems.Add(AppRegistrationProblem.Of(Codes.ManifestAppIdMismatch, $"apps[{i}]", "The manifest describes a different app"));
                app.Manifest = null;
            }

            entries.Add((i, app, appProblems));
        }

        var manifests = entries.Where(e => e.app.Manifest != null).Select(e => e.app.Manifest!).ToList();
        var plans = manifests.Count == 0
            ? []
            : await appRegistrationV2Service.PlanAsync(manifests, AppRegistrationV2Service.ValidationMode.InstallOrUpdate, odinContext);
        var planByApp = plans.ToDictionary(p => p.AppId);
        var registered = (await appRegistrationService.GetRegisteredAppsAsync(odinContext)).ToDictionary(a => a.AppId.Value);

        var previews = entries.Select(e =>
        {
            var existing = registered.GetValueOrDefault(e.app.AppId);
            var plan = planByApp.GetValueOrDefault(e.app.AppId);

            if (plan == null && existing == null)
            {
                e.problems.Add(AppRegistrationProblem.Of(Codes.AppNotRegistered, $"apps[{e.index}]",
                    "This app is not registered and the request did not include its manifest"));
            }

            if (existing?.IsRevoked ?? false)
            {
                e.problems.Add(AppRegistrationProblem.Of(Codes.AppRevoked, $"apps[{e.index}]", "This app is revoked; allow it again before including it"));
            }

            return new BundleAppPreview
            {
                AppId = e.app.AppId,
                Name = existing?.Name ?? plan?.Manifest.Name ?? "",
                AppSlug = existing?.AppSlug ?? plan?.Manifest.AppSlug ?? "",
                IsPrimary = e.app.AppId == request.PrimaryAppId,
                IsRegistered = existing != null,
                IsReserved = AppRegistrationV2Service.IsReserved(e.app.AppId),
                IsRevoked = existing?.IsRevoked ?? false,
                HasManifest = plan != null,
                Action = plan == null ? BundleAppAction.None
                    : existing == null ? BundleAppAction.Install
                    : plan.Diff.HasChanges ? BundleAppAction.Update
                    : BundleAppAction.None,
                Validation = plan?.ToResult(),
                Problems = e.problems
            };
        }).ToList();

        var primary = entries.FirstOrDefault(e => e.app.AppId == request.PrimaryAppId).app;
        if (primary != null)
        {
            var corsHostName = primary.Manifest?.CorsHostName ?? registered.GetValueOrDefault(primary.AppId)?.CorsHostName;
            var redirectProblem = BundleTokenService.RedirectProblem(corsHostName, request.RedirectUri);
            if (redirectProblem != null)
            {
                problems.Add(AppRegistrationProblem.Of(Codes.RedirectNotAllowed, "redirectUri", redirectProblem));
            }
        }

        return (new BundleAuthorizationPreview { Apps = previews, Problems = problems }, plans);
    }
}
