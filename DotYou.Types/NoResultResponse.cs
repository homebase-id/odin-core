namespace DotYou.Types
{
    /// <summary>
    /// Result when calling API functions like delete or create which do not require a complex response.
    /// 
    /// TODO: this name sucks.. and i'm only doing this for #prototrial until I figure out refit
    /// </summary>
    public class NoResultResponse
    {
        public NoResultResponse() { }
        public NoResultResponse(bool success)
        {
            Success = true;
        }
        public bool Success { get; set; }
    }
}
