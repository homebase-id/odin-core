using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.Acl
{
    public class AccessControlList
    {
        public SecurityGroupType RequiredSecurityGroup { get; set; }

        /// <summary>
        /// A list of circles which can access this file.  The caller must below to at least one circle. To retrieve the list, use <see cref="GetRequiredCircles"/>.
        /// </summary>
        public List<Guid> CircleIdList { get; set; }

        /// <summary>
        /// The list of DotYouIdentities allowed access when <see cref="RequiredSecurityGroup"/> = <see cref="SecurityGroupType.CustomList"/>
        /// </summary>
        public List<string> DotYouIdentityList { get; set; }

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

            return this.CircleIdList;
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

            return this.DotYouIdentityList;
        }

        public void Validate()
        {
            if (RequiredSecurityGroup == SecurityGroupType.Anonymous || RequiredSecurityGroup == SecurityGroupType.Owner)
            {
                if ((this.CircleIdList?.Count() ?? 0) > 0 || (this.DotYouIdentityList?.Count() ?? 0) > 0)
                {
                    throw new StorageException("Cannot specify circle or identity list when required security group is anonymous or owner");
                }
            }

            if (RequiredSecurityGroup == SecurityGroupType.Authenticated)
            {
                if ((this.CircleIdList?.Count() ?? 0) > 0)
                {
                    throw new StorageException("Cannot specify circle list when required security group is authenticated");
                }
            }
        }
    }
}