using Dawn;
using Youverse.Core.Services.Drives.DriveCore.Query;

namespace Youverse.Core.Services.Optimization.Cdn;

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