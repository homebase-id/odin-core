using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Util;

namespace Odin.Core.Services.Drives;

public class CollectionQueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNullOrEmpty(this.Name, nameof(this.Name));
        QueryParams.AssertIsValid();
    }
}