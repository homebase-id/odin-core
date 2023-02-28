using Dawn;
using Youverse.Core.Services.Drives.DriveCore.Query;

namespace Youverse.Core.Services.Drives;

public class CollectionQueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptions ResultOptions { get; set; }
    
    public void AssertIsValid()
    {
        Guard.Argument(this.Name, nameof(this.Name)).NotEmpty().NotNull();
        QueryParams.AssertIsValid();
    }
}