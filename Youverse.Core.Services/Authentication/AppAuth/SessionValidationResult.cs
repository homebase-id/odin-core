namespace Youverse.Core.Services.Authentication.AppAuth
{
    public class SessionValidationResult
    {
        public bool IsValid { get; init; }
        public AppDevice AppDevice { get; init; }
    }
}