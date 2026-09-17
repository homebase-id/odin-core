using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps.Builtin;

namespace Odin.Services.Authorization.Apps;

/// <summary>
/// Who may own a drive or a circle.
/// </summary>
/// <remarks>
/// One definition, because drives and circles have to agree on it: the owning app is half a drive's
/// wire address (<c>/apps/{appSlug}/drives/{driveSlug}</c>) and names who may administer a circle, and
/// neither is changeable afterwards except by an explicit hand-over.  A row written against an app
/// nobody registered is therefore stuck at an address that resolves to nothing, which is why this is
/// checked when the row is written rather than trusted.
/// <para>
/// Takes the table rather than <c>IAppRegistrationService</c> deliberately.  That service depends on
/// <c>ExchangeGrantService</c>, which depends on <c>IDriveManager</c> -- so a drive-side caller cannot
/// reach it (the cycle is documented on <c>OwnerDriveManagementController</c>).  The table sits below
/// the cycle and both callers already hold it.
/// </para>
/// </remarks>
public static class OwningApp
{
    /// <summary>
    /// Throws unless the app exists: one the platform ships, or one registered on this identity.
    /// </summary>
    /// <remarks>
    /// A null passes.  It means the owner acting as themselves, which callers resolve to
    /// <see cref="Odin.Services.Apps.SystemAppConstants.OwnerConsoleAppId"/> -- itself a platform app,
    /// so spelling it out here would only restate what <see cref="BuiltinApps.IsPlatformApp"/> answers.
    /// </remarks>
    public static async Task AssertExistsAsync(TableAppRegistrations appRegistrations, Guid? appId)
    {
        if (appId == null || BuiltinApps.IsPlatformApp(appId.Value))
        {
            return;
        }

        if (await appRegistrations.GetAsync(appId.Value) == null)
        {
            throw new OdinClientException($"No app is registered with id {appId}",
                OdinClientErrorCode.AppNotRegistered);
        }
    }
}
