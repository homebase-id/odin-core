using System;

namespace Youverse.Core.Services.Drive.Security
{
    public class GranteeIdentity
    {
        public Guid Id { get; set; }
        
        public string DomainIdentity { get; set; }
    }
}