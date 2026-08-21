using System.Text.Json.Serialization;

namespace Odin.Services.Authorization.Acl
{
    public enum SecurityGroupType
    {
        /// <summary>
        /// Indicates anyone on the internet (i.e. public)
        /// </summary>
        Anonymous = 111,

        // TODO: Requests where the caller is not on the odin network yet holds an x-token for accessing data
        //YouAuthExchange = 333,

        /// <summary>
        /// Any logged-in Homebase identity, and every connection the owner has not yet reviewed.
        /// An introduced stranger reads nothing here beyond what any authenticated identity reads.
        /// </summary>
        Authenticated = 444,

        /// <summary>
        /// Connections the owner completed the connection review for.  Includes every circle member by
        /// construction, since being added to a circle implies the review.
        /// <para>
        /// This is the slot formerly named <c>Connected</c>.  It is for low-sensitivity social surfaces --
        /// the connections list, who-I-follow, reacting to secured posts -- and never for high-sensitivity
        /// data, which stays on enumerated circle ACLs.
        /// </para>
        /// <para>
        /// The serialized value stays <c>connected</c>: enums persist as camelCase strings (see
        /// <c>OdinSystemSerializer</c>), so every stored ACL and every deployed client already carries that
        /// spelling.  Only the member name and the UX labels change.
        /// </para>
        /// </summary>
        [JsonStringEnumMemberName("connected")]
        Reviewed = 777,

        /// <summary>
        /// Specifies that only the owner can access a file
        /// </summary>
        Owner = 999,

        System = 1
    }
}
