using Youverse.Core.Services.Authorization.Acl;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadFileMetadata
    {
        public UploadFileMetadata()
        {
            this.AppData = new();
            this.AccessControlList = new AccessControlList() {RequiredSecurityGroup = SecurityGroupType.Owner};
        }

        public string ContentType { get; set; }

        public string SenderDotYouId { get; set; }

        public AccessControlList AccessControlList { get; set; }

        public UploadAppFileMetaData AppData { get; set; }
    }
}