using System;
using DotYou.Types.DataAttribute;

namespace DotYou.Types
{
    public class HumanProfile
    {
        private DotYouIdentity _dotYouId;
        private Guid _id;

        /// <summary>
        /// 
        /// </summary>
        public Guid Id
        {
            get => _id;
            set
            {
                //HACK: no-op since this is set by the DotYouId prop.  Leaving set in place so the Json Deserializer can call it. 
            }
        }

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

        public NameAttribute Name { get; set; }

        /// <summary>
        /// A base64 string of this <see cref="DotYouId"/>'s public key certificate.
        /// </summary>
        public string PublicKeyCertificate { get; set; }

        public string AvatarUri { get; set; }
    }
}