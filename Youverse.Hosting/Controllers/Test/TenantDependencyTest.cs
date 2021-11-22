using System;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Youverse.Hosting.Controllers.Test
{
    public interface ITenantDependencyTest
    {
        string Hello(string name);
    }

    public class TenantDependencyTest : ITenantDependencyTest, IDisposable
    {
        private readonly Guid _id;
        private readonly ILogger<TenantDependencyTest> _logger;
        
        public TenantDependencyTest(ILogger<TenantDependencyTest> logger)
        {
            _id = Guid.NewGuid();
            _logger = logger;
        }
        
        public string Hello(string name)
        {
            _logger.LogInformation("HELLO {name} FROM {id}", name, _id);
            return $"HELLO {name} FROM {_id}";
        }

        public void Dispose()
        {
            ;
        }
    }
    
    public interface ITenantDependencyTest2
    {
        string Hello(string name);
    }

    public class TenantDependencyTest2 : ITenantDependencyTest2, IDisposable
    {
        private readonly Guid _id;
        private readonly ILogger<TenantDependencyTest2> _logger;
        
        public TenantDependencyTest2(ILogger<TenantDependencyTest2> logger)
        {
            _id = Guid.NewGuid();
            _logger = logger;
        }
        
        public string Hello(string name)
        {
            _logger.LogInformation("HELLO2 {name} FROM {id}", name, _id);
            return $"HELLO2 {name} FROM {_id}";
        }

        public void Dispose()
        {
            ;
        }
    }
    
}