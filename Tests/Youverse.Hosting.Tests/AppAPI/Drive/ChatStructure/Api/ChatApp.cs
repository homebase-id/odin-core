using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatApp
{
    private DotYouIdentity _sender;
    private TestSampleAppContext _appContext;
    private Dictionary<Guid, List<string>> _groups;
    public ChatCommandService CommandService { get; }
    public ChatMessageService MessageService { get; }

    public ChatApp(DotYouIdentity sender, TestSampleAppContext appContext, WebScaffold scaffold)
    {
        _sender = sender;

        _appContext = appContext;
        var ctx = new ChatContext(sender, appContext, scaffold);
        CommandService = new ChatCommandService(ctx);
        MessageService = new ChatMessageService(ctx);
    }
}