namespace Odin.Hosting.UnifiedV2;

public static class UnifiedApiRouteConstants
{
    public const string BasePath = "/api/v2";
    public const string Auth = BasePath + "/auth";
    public const string Drive = BasePath + "/drive";
    public const string Files = Drive + "/files";
    public const string Query = Drive + "/query";
}