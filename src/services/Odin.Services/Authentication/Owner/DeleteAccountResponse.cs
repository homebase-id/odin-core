using Odin.Core.Time;

namespace Odin.Services.Authentication.Owner;

public class DeleteAccountResponse
{
    public UnixTimeUtc PlannedDeletionDate { get; set; }
}