using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.PubSub;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.PubSub;

#nullable enable

public class InProcPubSubTests
{
    private ILifetimeScope RegisterServicesAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });


        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.AddSystemPubSub(false);
        builder.AddTenantPubSub("frodo.me", false);

        return builder.Build();
    }

    //

    [Test]
    public async Task SenderShouldReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));
    }

    //

    [Test]
    public async Task ItShouldSendAndReceiveOnMultipleChannels()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel1 = "test-channel1";
        const string testChannel2 = "test-channel2";

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

        var pubSub1Channel1MessageReceived = "";
        var pubSub2Channel1MessageReceived = "";
        var pubSub1Channel2MessageReceived = "";
        var pubSub2Channel2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel1, MessageFromSelf.Process, async message =>
        {
            pubSub1Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel1, MessageFromSelf.Process, async message =>
        {
            pubSub2Channel1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub1.SubscribeAsync<string>(testChannel2, MessageFromSelf.Process, async message =>
        {
            pubSub1Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel2, MessageFromSelf.Process, async message =>
        {
            pubSub2Channel2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel1, "Hello");
        await pubSub2.PublishAsync(testChannel2, "There");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2Channel1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub1Channel2MessageReceived, Is.EqualTo("There"));
        Assert.That(pubSub2Channel2MessageReceived, Is.EqualTo("There"));
    }

    //

    [Test]
    public async Task SenderShouldNotReceiveItsOwnMessages()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        await pubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo(""));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));
    }

    //

    [Test]
    public async Task ItShouldUnsubscribe()
    {
        const string channelPrefix = "my-prefix";
        const string testChannel = "test-channel";

        var logger = TestLogFactory.CreateConsoleLogger<InProcPubSub>();

        var pubSub1 = new InProcPubSub(logger, channelPrefix);
        var pubSub2 = new InProcPubSub(logger, channelPrefix);

        var pubSub1MessageReceived = "";
        var pubSub2MessageReceived = "";

        await pubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub1MessageReceived = message;
            await Task.CompletedTask;
        });

        var unsubscribeToken = await pubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
        {
            pubSub2MessageReceived = message;
            await Task.CompletedTask;
        });

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));

        pubSub1MessageReceived = "";
        pubSub2MessageReceived = "";

        await pubSub2.UnsubscribeAsync(testChannel, unsubscribeToken);

        await Task.Delay(500); // Give some time for subscriptions to be set up

        await pubSub1.PublishAsync(testChannel, "Hello again");

        await Task.Delay(500); // Give some time for messages to be processed

        Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello again"));
        Assert.That(pubSub2MessageReceived, Is.EqualTo(""));
    }

    //

    // [Test]
    // public async Task SystemDoesReceiveMessagesFromSelf()
    // {
    //     const string testChannel = "test-channel";
    //
    //     var services = RegisterServicesAsync();
    //
    //     var systemPubSub = services.Resolve<ISystemPubSub>();
    //
    //     var systemReceived = false;
    //
    //     await systemPubSub.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
    //     {
    //         systemReceived = true;
    //         await Task.CompletedTask;
    //     });
    //
    //     await Task.Delay(500); // Give some time for subscriptions to be set up
    //
    //     await systemPubSub.PublishAsync(testChannel, "Hello from System!");
    //
    //     await Task.Delay(500); // Give some time for messages to be processed
    //
    //     Assert.That(systemReceived, Is.True);
    //
    //     services.Dispose();
    // }
    //
    //
    // //
    //
    // [Test]
    // public async Task SystemDoesntReceiveMessagesFromSelf()
    // {
    //     const string testChannel = "test-channel";
    //
    //     var services = RegisterServicesAsync();
    //
    //     var systemPubSub = services.Resolve<ISystemPubSub>();
    //
    //     var systemReceived = false;
    //
    //     await systemPubSub.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
    //     {
    //         systemReceived = true;
    //         await Task.CompletedTask;
    //     });
    //
    //     await Task.Delay(500); // Give some time for subscriptions to be set up
    //
    //     await systemPubSub.PublishAsync(testChannel, "Hello from System!");
    //
    //     await Task.Delay(500); // Give some time for messages to be processed
    //
    //     Assert.That(systemReceived, Is.False);
    //
    //     services.Dispose();
    // }
    //
    // //
    //
    // [Test]
    // public async Task TenantDoesReceiveMessagesFromSelf()
    // {
    //     const string testChannel = "test-channel";
    //
    //     var services1 = RegisterServicesAsync();
    //     var services2 = RegisterServicesAsync();
    //
    //     var tenantPubSub1 = services1.Resolve<ITenantPubSub>();
    //     var tenantPubSub2 = services2.Resolve<ITenantPubSub>();
    //
    //     var pubSub1MessageReceived = "";
    //     var pubSub2MessageReceived = "";
    //
    //     await tenantPubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
    //     {
    //         pubSub1MessageReceived = message;
    //         await Task.CompletedTask;
    //     });
    //
    //     await tenantPubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Process, async message =>
    //     {
    //         pubSub2MessageReceived = message;
    //         await Task.CompletedTask;
    //     });
    //
    //     await Task.Delay(500); // Give some time for subscriptions to be set up
    //
    //     await tenantPubSub1.PublishAsync(testChannel, "Hello");
    //
    //     await Task.Delay(500); // Give some time for messages to be processed
    //
    //     Assert.That(pubSub1MessageReceived, Is.EqualTo("Hello"));
    //     Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));
    //
    //     services1.Dispose();
    //     services2.Dispose();
    // }
    //
    // //
    //
    // [Test]
    // public async Task TenantDoesntReceiveMessagesFromSelf()
    // {
    //     const string testChannel = "test-channel";
    //
    //     var services1 = RegisterServicesAsync();
    //     var services2 = RegisterServicesAsync();
    //
    //     var tenantPubSub1 = services1.Resolve<ITenantPubSub>();
    //     var tenantPubSub2 = services2.Resolve<ITenantPubSub>();
    //
    //     var pubSub1MessageReceived = "";
    //     var pubSub2MessageReceived = "";
    //
    //     await tenantPubSub1.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
    //     {
    //         pubSub1MessageReceived = message;
    //         await Task.CompletedTask;
    //     });
    //
    //     await tenantPubSub2.SubscribeAsync<string>(testChannel, MessageFromSelf.Ignore, async message =>
    //     {
    //         pubSub2MessageReceived = message;
    //         await Task.CompletedTask;
    //     });
    //
    //     await Task.Delay(500); // Give some time for subscriptions to be set up
    //
    //     await tenantPubSub1.PublishAsync(testChannel, "Hello");
    //
    //     await Task.Delay(500); // Give some time for messages to be processed
    //
    //     Assert.That(pubSub1MessageReceived, Is.EqualTo(""));
    //     Assert.That(pubSub2MessageReceived, Is.EqualTo("Hello"));
    //
    //     services1.Dispose();
    //     services2.Dispose();
    // }

    //



    //

}

