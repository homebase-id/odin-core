using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.Cdn;

public class QueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public SectionResultOptions ResultOptions { get; set; }
}