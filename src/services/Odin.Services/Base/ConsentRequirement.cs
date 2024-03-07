#nullable enable
using System;
using Odin.Core.Exceptions;
using Odin.Core.Time;

namespace Odin.Services.Base;

public enum ConsentRequirementType
{
    /// <summary>
    /// Always require consent
    /// </summary>
    Always = 10,

    /// <summary>
    /// Require consent after a certain expiration
    /// </summary>
    Expiring = 32,

    /// <summary>
    /// Never require consent
    /// </summary>
    Never = 0
}

public class ConsentRequirements
{
    public static ConsentRequirements Default = new ConsentRequirements() { ConsentRequirementType = ConsentRequirementType.Always };
    public ConsentRequirementType ConsentRequirementType { get; set; }

    /// <summary>
    /// Indicates a future date when auto-approval/auto-consent expires
    /// </summary>
    public UnixTimeUtc Expiration { get; set; }

    public void Validate()
    {
        if (this.ConsentRequirementType == ConsentRequirementType.Expiring)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (this.Expiration.milliseconds <= nowMs)
            {
                throw new OdinClientException("Consent expiration must be a future date when ConsentRequirementType == Expiring");
            }
        }
    }

    public bool IsRequired()
    {
        if (this.ConsentRequirementType == ConsentRequirementType.Always)
        {
            return true;
        }

        if (this.ConsentRequirementType == ConsentRequirementType.Expiring)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return nowMs > this.Expiration.milliseconds;
        }

        if (this.ConsentRequirementType == ConsentRequirementType.Never)
        {
            return false;
        }

        return true;
    }
}