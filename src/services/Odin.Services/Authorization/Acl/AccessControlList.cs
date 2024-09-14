using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Odin.Core.Exceptions;

namespace Odin.Services.Authorization.Acl
{
    [DebuggerDisplay("RSG:{RequiredSecurityGroup}, Circles:{RequiredCircleCount} OdinIds:{RequiredOdinIdCount}")]
    public class AccessControlList
    {
        public static AccessControlList OwnerOnly => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Owner };
        public static AccessControlList Authenticated => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Authenticated };
        public static AccessControlList Connected => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.ConfirmConnected };
        public static AccessControlList Anonymous => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous };

        public SecurityGroupType RequiredSecurityGroup { get; set; }

        /// <summary>
        /// A list of circles which can access this file.  The caller must below to at least one circle. To retrieve the list, use <see cref="GetRequiredCircles"/>.
        /// </summary>
        public List<Guid> CircleIdList { get; set; }

        /// <summary>
        /// The list of OdinId allowed access the file.
        /// </summary>
        public List<string> OdinIdList { get; set; }

        /// <summary>
        /// Returns the list of Circles that can access the file when the RequiredSecurityGroup is SecurityGroupType.CircleConnected
        /// </summary>
        public IEnumerable<Guid> GetRequiredCircles()
        {
            if (RequiredSecurityGroup is SecurityGroupType.Anonymous or SecurityGroupType.Owner)
            {
                //anonymous files don't allow circles to be set.
                //owner can view all files
                return Array.Empty<Guid>();
            }

            return (IEnumerable<Guid>)this.CircleIdList ?? Array.Empty<Guid>();
        }

        /// <summary>
        /// Returns the list of Identities that can access the file when the RequiredSecurityGroup is SecurityGroupType.CustomList
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetRequiredIdentities()
        {
            if (RequiredSecurityGroup is SecurityGroupType.Anonymous or SecurityGroupType.Owner)
            {
                //anonymous files don't allow an identity list
                //owner can view all files
                return Array.Empty<string>();
            }

            return (IEnumerable<string>)this.OdinIdList ?? Array.Empty<string>();
        }

        public void Validate()
        {
            if (RequiredSecurityGroup == SecurityGroupType.Anonymous || RequiredSecurityGroup == SecurityGroupType.Owner)
            {
                if ((this.CircleIdList?.Count() ?? 0) > 0 || (this.OdinIdList?.Count() ?? 0) > 0)
                {
                    throw new OdinClientException("Cannot specify circle or identity list when required security group is anonymous or owner",
                        OdinClientErrorCode.CannotAllowCirclesOrIdentitiesOnAnonymousOrOwnerOnly);
                }
            }

            //Note: this is now allowed because of YouAuth Domains`
            // if (RequiredSecurityGroup == SecurityGroupType.Authenticated)
            // {
            //     if ((this.CircleIdList?.Count() ?? 0) > 0)
            //     {
            //         throw new OdinClientException("Cannot specify circle list when required security group is authenticated", OdinClientErrorCode.CannotAllowCirclesOnAuthenticatedOnly);
            //     }
            // }
        }

        //

        private int RequiredCircleCount => CircleIdList?.Count ?? 0;

        private int RequiredOdinIdCount => OdinIdList?.Count ?? 0;
    }
}