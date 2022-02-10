using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Authorization.Acl
{
    public class AccessControlList
    {
        //list of enties that can read this file

        public SecurityGroupType RequiredSecurityGroup { get; set; }
        
        /// <summary>
        /// The circle required by the caller when <see cref="RequiredSecurityGroup"/>  = <see cref="SecurityGroupType.CircleConnected"/>
        /// </summary>
        public Guid CircleId { get; set; }
        
        /// <summary>
        /// The list of DotYouIdentities allowed access when <see cref="RequiredSecurityGroup"/> = <see cref="SecurityGroupType.CustomList"/>
        /// </summary>
        public List<string> DotYouIdentityList { get; set; }
    }
}