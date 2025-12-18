using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Util;

namespace Odin.Services.Optimization.Cdn;

public class QueryParamSection
{
    public string Name { get; set; }

    public FileQueryParamsV1 QueryParams { get; set; }

    public SectionResultOptions ResultOptions { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNull(this.Name, nameof(this.Name));
        QueryParams.AssertIsValid();
    }
}