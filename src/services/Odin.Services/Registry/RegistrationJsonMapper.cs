using System;
using Odin.Core.Serialization;
using Odin.Core.Time;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// Reads and writes the Registrations json column, which holds the <see cref="TenantStatusState"/> and the
/// <see cref="RegistrationInvitation"/> the identity signed up with. Kept out of dedicated columns so it can grow without a
/// schema change.
/// </summary>
public static class RegistrationJsonMapper
{
    // The status fields stay at the top level, where nodes that only know TenantStatusState read them
    private sealed record RegistrationJson(
        TenantStatus Status,
        DisabledReason? DisabledReason,
        UnixTimeUtc? StatusChangedAt,
        RegistrationInvitation? Invitation);

    public static string ToJson(IdentityRegistration registration)
    {
        return Serialize(registration.StatusState, registration.Invitation);
    }

    /// <summary>
    /// <paramref name="existingJson"/> with its status replaced by <paramref name="state"/>, keeping everything else.
    /// </summary>
    public static string WithStatus(string? existingJson, TenantStatusState state)
    {
        return Serialize(state, TryParse(existingJson, out _)?.Invitation);
    }

    private static string Serialize(TenantStatusState state, RegistrationInvitation? invitation)
    {
        return OdinSystemSerializer.Serialize(new RegistrationJson(state.Status, state.DisabledReason,
            state.StatusChangedAt, invitation));
    }

    /// <summary>
    /// Works out the status from the legacy disabled column and the json column.
    /// </summary>
    /// <remarks>
    /// The disabled column wins for the disabled bit. Nodes running a version without the json
    /// column still read and write it during a rolling deploy, and they null the json on every save,
    /// so a disagreement means an older node changed the disabled flag after this json was written.
    /// </remarks>
    /// <returns>False if the json was present but unreadable; the status then comes from the column alone.</returns>
    public static bool Apply(IdentityRegistration target, bool disabledColumn, string? json)
    {
        var parsed = TryParse(json, out var valid);
        target.Invitation = parsed?.Invitation;

        if (disabledColumn)
        {
            target.Status = TenantStatus.Disabled;
            if (parsed?.Status == TenantStatus.Disabled)
            {
                target.DisabledReason = parsed.DisabledReason ?? DisabledReason.Admin;
                target.StatusChangedAt = parsed.StatusChangedAt;
            }
            else
            {
                target.DisabledReason = DisabledReason.Admin;
                target.StatusChangedAt = null;
            }
        }
        else if (parsed == null || parsed.Status == TenantStatus.Disabled)
        {
            target.Status = TenantStatus.Active;
            target.DisabledReason = null;
            target.StatusChangedAt = null;
        }
        else
        {
            target.Status = parsed.Status;
            target.DisabledReason = null;
            target.StatusChangedAt = parsed.StatusChangedAt;
        }

        return valid;
    }

    private static RegistrationJson? TryParse(string? json, out bool valid)
    {
        valid = true;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = OdinSystemSerializer.Deserialize<RegistrationJson>(json);
            if (parsed != null &&
                Enum.IsDefined(parsed.Status) &&
                (!parsed.DisabledReason.HasValue || Enum.IsDefined(parsed.DisabledReason.Value)))
            {
                return parsed;
            }
        }
        catch (Exception)
        {
            // Unreadable json falls back to the disabled column
        }

        valid = false;
        return null;
    }
}
