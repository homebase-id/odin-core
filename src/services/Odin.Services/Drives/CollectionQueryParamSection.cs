using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Util;

namespace Odin.Services.Drives;

public class CollectionQueryParamSection
{
    public string Name { get; set; }

    public FileQueryParamsV1 QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNullOrEmpty(this.Name, nameof(this.Name));
        QueryParams.AssertIsValid();
    }
}