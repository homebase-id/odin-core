using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Storage
{
    public class AccessControlList
    {
        //list of enties that can read this file

        public SecurityGroupType SecurityGroup { get; set; }
        
        /// <summary>
        /// The circle required by the caller when <see cref="SecurityGroup"/>  = <see cref="SecurityGroupType.CircleConnected"/>
        /// </summary>
        public int CircleId { get; set; }
        
        /// <summary>
        /// The list of DotYouIdentities allowed access when <see cref="SecurityGroup"/> = <see cref="SecurityGroupType.CustomList"/>
        /// </summary>
        public List<string> DotYouIdentityList { get; set; }
    }
}