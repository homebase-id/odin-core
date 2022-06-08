using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Authorization.Acl
{
    public class AccessControlList
    {
        public SecurityGroupType RequiredSecurityGroup { get; set; }
        
        /// <summary>
        /// The circle required by the caller when <see cref="RequiredSecurityGroup"/>  = <see cref="SecurityGroupType.CircleConnected"/>
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
            if (RequiredSecurityGroup == SecurityGroupType.CircleConnected)
            {
                return this.CircleIdList;
            }

            return Array.Empty<Guid>();
        }
        
        /// <summary>
        /// Returns the list of Identities that can access the file when the RequiredSecurityGroup is SecurityGroupType.CustomList
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Guid> GetRequiredIdentities()
        {
            //TODO: need to get IDs for each domain form the global map that's not yet created :)
            // if (RequiredSecurityGroup == SecurityGroupType.CustomList)
            // {
            //     return this.DotYouIdentityList
            // }

            return Array.Empty<Guid>();
        }
    }
}