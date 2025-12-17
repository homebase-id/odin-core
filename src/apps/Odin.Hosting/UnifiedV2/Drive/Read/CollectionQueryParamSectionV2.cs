using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class CollectionQueryParamSectionV2
{
    public string Name { get; set; }

    public FileQueryParamsV2 QueryParams { get; set; }

    public QueryBatchResultOptionsRequestV2 ResultOptionsRequest { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNullOrEmpty(this.Name, nameof(this.Name));
        QueryParams.AssertIsValid();
    }
}