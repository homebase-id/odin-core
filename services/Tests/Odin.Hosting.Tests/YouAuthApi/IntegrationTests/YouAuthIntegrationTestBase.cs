using System.Reflection;
using NUnit.Framework;

//
// OWNER AUTHENTICATION:
//   COOKIE 'DY0810':
//     - Half-key (byte array): Half of the zero-knowledge key to "access" identity's resources on the server.
//       The half-key is stored in the ```DY0810``` by the authentication controller.
//     - Session ID (uuid)
    
//
//   Response when cookie is created:
//     Shared secret: the shared key for doing symmetric encryption client<->server (on top of TLS).
//     It is generated as part of authentication and is returned to the client by the authentication controller.
//     
// HOME AUTHENTICATION:
//
//    COOKIE 'XT32':    
// 
//   
// APP HEADER: BX0900
//
// 
//   
//

#nullable enable
namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests;

public abstract class YouAuthIntegrationTestBase
{
    protected WebScaffold Scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        Scaffold = new WebScaffold(folder);
        Scaffold.RunBeforeAnyTests();
    }

    //

    [TearDown]
    public void Cleanup()
    {
        Scaffold.RunAfterAnyTests();
    }
}