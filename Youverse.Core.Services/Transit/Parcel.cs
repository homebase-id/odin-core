using System;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class Parcel
    {
        public Parcel(Guid fileId)
        {
            this.FileId = fileId;
        }

        public RecipientList RecipientList { get; set; }

        public Guid FileId { get; set; }
        
    }
}