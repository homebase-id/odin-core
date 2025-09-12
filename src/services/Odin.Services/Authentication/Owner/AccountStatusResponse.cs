using Odin.Core.Time;

namespace Odin.Services.Authentication.Owner;

public class AccountStatusResponse
{
    public UnixTimeUtc? PlannedDeletionDate { get; set; }
    public string PlanId { get; set; }
}