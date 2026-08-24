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
        /// Legacy. Never assigned to a caller, and no longer meaningful to the evaluator -- but stored
        /// file headers carry it, so the member has to stay for those to deserialize.
        /// </summary>
        /// <remarks>
        /// The value was only ever set on files by clients (the chat app ACLs messages with it), never by
        /// a caller context, and the ACL evaluator always folded it into the 777 case. Deleting it would
        /// throw on every existing header that names it. Retires for real once no stored file references
        /// it -- with the system circles, in the enrollment phase.
        /// <para>
        /// Do not use it for anything new. Keep the value at 555: the indexed
        /// <c>requiredSecurityGroup</c> column already holds 555 for those rows, and the enum and the
        /// column must agree.
        /// </para>
        /// </remarks>
        AutoConnected = 555,

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
