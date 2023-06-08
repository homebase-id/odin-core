using Odin.Core.Identity;

namespace Odin.Core.Services.Contacts.Circle.Membership;

public record CircleMemberStorageData
{
    public OdinId OdinId { get; set; }
    public CircleGrant CircleGrant { get; set; }
}