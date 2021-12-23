using System;

namespace Youverse.Core.Services.Tests.Drive
{
    public class TestFileProps
    {
        public object MetadataJsonContent { get; set; }
        public string PayloadContentType { get; set; }
        public string PayloadData { get; set; }
        public Guid? CategoryId { get; set; }
        public bool ContentIsComplete { get; set; }
    }
}