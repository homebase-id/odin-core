namespace Odin.Test.Helpers.Secrets;

public static class TestSecrets
{
    public static void Load()
    {
        // WARNING: make sure the file below is not checked into source control
        DotNetEnv.Env.Load("secrets.env");
    }
}
