using Autofac;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Logging.Hostname;

namespace Youverse.Core.Services.Logging
{
    public class LoggingAutofacModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<CorrelationUniqueIdGenerator>().As<ICorrelationIdGenerator>().SingleInstance();
            builder.RegisterType<CorrelationContext>().As<ICorrelationContext>().SingleInstance();
            builder.RegisterType<StickyHostname>().As<IStickyHostname>().SingleInstance();
            builder.RegisterType<StickyHostnameGenerator>().As<IStickyHostnameGenerator>().SingleInstance();
        }
    }
}