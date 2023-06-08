using Dawn;
using Odin.Core.Services.Drives.DriveCore.Query;

namespace Odin.Core.Services.Optimization.Cdn;

public class QueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public SectionResultOptions ResultOptions { get; set; }
    
    public void AssertIsValid()
    {
        Guard.Argument(this.Name, nameof(this.Name)).NotEmpty().NotNull();
        QueryParams.AssertIsValid();
        
    }
}