namespace Youverse.Provisioning.Services.Registration
{
    /// <summary>
    /// Base class for services in this Registry.  It offers various methods required by most services.
    /// </summary>
    public abstract class RegistryServiceBase
    {
        ILogger _logger;

        public RegistryServiceBase(ILogger logger)
        {
            _logger = logger;
        }
    }
}