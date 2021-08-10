using System;
using DotYou.Types.DataAttribute;

namespace DotYou.Types
{
    public class HumanProfile
    {
        private DotYouIdentity _dotYouId;
        
        public Guid Id { get; set; }
        
        /// <summary>
        /// Specifies the DI address for this Human
        /// </summary>
        public DotYouIdentity DotYouId
        {
            get => _dotYouId;
            set
            {
                _dotYouId = value;
                this.Id = MiscUtils.MD5HashToGuid(_dotYouId);
            }
        }

        public NameAttribute Name
        {
            get;
            set;
        }
        
        /// <summary>
        /// A base64 string of this <see cref="DotYouId"/>'s public key certificate.
        /// </summary>
        public string PublicKeyCertificate { get; set; }
        
    }
}
