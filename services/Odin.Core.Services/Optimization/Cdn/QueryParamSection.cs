using Dawn;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Util;

namespace Odin.Core.Services.Optimization.Cdn;

public class QueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public SectionResultOptions ResultOptions { get; set; }

    public void AssertIsValid()
    {
        OdinValidationUtils.AssertNotNull(this.Name, nameof(this.Name));
        QueryParams.AssertIsValid();
    }
}