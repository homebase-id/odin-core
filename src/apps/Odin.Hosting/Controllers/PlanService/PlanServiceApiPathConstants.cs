namespace Odin.Hosting.Controllers.PlanService;

public static class PlanServiceApiPathConstants
{
    public const string RootPath = "/api/plans";
    public const string BasePathV1 = RootPath + "/v1";

    // The header homebase-invites already takes its own keys in
    public const string ApiKeyHeader = "Odin-Api-Key";
}
