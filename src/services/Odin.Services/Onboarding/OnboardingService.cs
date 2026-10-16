using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Registry;

namespace Odin.Services.Onboarding;

#nullable enable

/// <summary>
/// The identity's own copy of its <see cref="OnboardingInfo"/> (#1908). For the owner console and the Chat
/// app only: other apps cannot read it, and only Chat marks it handled.
/// </summary>
public class OnboardingService(TenantContext tenantContext, IIdentityRegistry registry, IdentityDatabase identityDatabase)
{
    private static readonly Guid StorageKey = Guid.Parse("4f0c7d1e-5a2b-4e89-9c3d-8b6a1f2e7d40");

    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("a3e6b2c9-1d47-4f85-b0e2-6c9d8a5f3b17"));

    /// <summary>
    /// Copies what signup handed over on the registration into the identity's database, at first run.
    /// Once there it is never copied again, so a handled record stays handled.
    /// </summary>
    public async Task CopyFromRegistrationAsync()
    {
        if (await ReadAsync() != null)
        {
            return;
        }

        var registration = await registry.GetAsync(tenantContext.HostOdinId.DomainName);
        if (registration?.Onboarding != null)
        {
            await Storage.UpsertAsync(identityDatabase.KeyValueCached, StorageKey, registration.Onboarding);
        }
    }

    //

    /// <summary>
    /// The identity's onboarding info; all empty for an identity that has none.
    /// </summary>
    public async Task<OnboardingInfo> GetAsync(IOdinContext odinContext)
    {
        if (!odinContext.Caller.HasMasterKey && !IsChat(odinContext))
        {
            throw new OdinSecurityException("Onboarding info is for the owner and the Chat app only");
        }

        return await ReadAsync() ?? new OnboardingInfo();
    }

    //

    /// <summary>
    /// Records that Chat has acted on the onboarding info. Only the first call sets it.
    /// </summary>
    public async Task<OnboardingInfo> MarkHandledAsync(IOdinContext odinContext)
    {
        if (!IsChat(odinContext))
        {
            throw new OdinSecurityException("Only the Chat app marks onboarding handled");
        }

        var info = await ReadAsync();
        if (info == null || info.HandledAt != null)
        {
            return info ?? new OnboardingInfo();
        }

        var handled = info with { HandledAt = UnixTimeUtc.Now() };
        await Storage.UpsertAsync(identityDatabase.KeyValueCached, StorageKey, handled);
        return handled;
    }

    //

    private async Task<OnboardingInfo?> ReadAsync()
    {
        return await Storage.GetAsync<OnboardingInfo>(identityDatabase.KeyValueCached, StorageKey);
    }

    private static bool IsChat(IOdinContext odinContext)
    {
        return odinContext.Caller.IsOwner &&
               odinContext.Caller.OdinClientContext?.AppId?.Value == SystemAppConstants.ChatAppId;
    }
}
