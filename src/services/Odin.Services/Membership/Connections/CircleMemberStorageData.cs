using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Util;
using Odin.Services.Membership.CircleMembership;

namespace Odin.Services.Membership.Connections;

public record CircleMemberStorageData
{
    public AsciiDomainName DomainName { get; set; }
    public CircleGrant CircleGrant { get; set; }
    
    public DomainType DomainType { get; set; }

    /// <summary>What a membership row's <c>data</c> holds.</summary>
    public static CircleMemberStorageData From(CircleMemberRecord row) =>
        OdinSystemSerializer.Deserialize<CircleMemberStorageData>(row.data.ToStringFromUtf8Bytes());
}