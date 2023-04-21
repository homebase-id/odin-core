using Youverse.Core.Identity;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

public record CircleMemberStorageData
{
    public OdinId OdinId { get; set; }
    public CircleGrant CircleGrant { get; set; }
}