namespace DotYou.IdentityRegistry
{
    public interface IIdentityContextRegistry
    {
        void Initialize();
        DotYouContext ResolveContext(string domainName);
    }
}