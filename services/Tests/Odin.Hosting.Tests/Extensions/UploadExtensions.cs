using System.Linq;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests.Extensions;

using System.Collections.Generic;

public static class UploadExtensions
{
    public static IEnumerable<UploadedPayloadDescriptor> ToPayloadDescriptorList(this IEnumerable<TestPayloadDefinition> list)
    {
        return list.Select(tpd => tpd.ToPayloadDescriptor());
    }
    
}