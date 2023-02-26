using System;

namespace Youverse.Core.Identity
{
    /// <summary>
    /// Base class for any which need their ID to be based on the <see cref="OdinId"/>.  This is useful for storage in LiteDB
    /// </summary>
    public abstract class DotYouIdBase
    {
        //used as the storage Id for LiteDB
        public Guid Id
        {
            get { return this.DotYouId; }
            set
            {
                //no-op
            }
        }

        /// <summary>
        /// Specifies the DI address for this Human
        /// </summary>
        public OdinId DotYouId { get; init; }
    }
}