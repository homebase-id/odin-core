namespace Odin.Test.Helpers.Secrets;

public static class TestSecrets
{
    public static void Load()
    {
        DotNetEnv.Env.Load("secrets.env");
    }
}
