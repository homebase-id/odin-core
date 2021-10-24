using System;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class Parcel
    {
        public Parcel(string storageRoot)
        {
            EncryptedFile = new EncryptedFile(storageRoot);
        }

        public RecipientList RecipientList { get; set; }

        public EncryptedFile EncryptedFile { get; set; }
    }
}