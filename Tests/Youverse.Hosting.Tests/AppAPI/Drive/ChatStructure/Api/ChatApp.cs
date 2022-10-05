using System;
using System.Collections.Generic;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatData
{
    public Dictionary<Guid, ChatGroupMessage> GroupMessages { get; } = new();
    public List<ChatMessage> Messages { get; } = new List<ChatMessage>();
    public List<ChatGroup> Groups { get; } = new List<ChatGroup>();
}

public class ChatApp
{
    private readonly ChatServerContext _chatServerContext;
    private readonly ChatData _chatData;
    private readonly ChatSynchronizer _synchronizer;

    private string _latestCursor = "";
    public ChatMessageService MessageService { get; }

    public ChatApp(TestSampleAppContext appContext, WebScaffold scaffold)
    {
        _chatServerContext = new ChatServerContext(appContext, scaffold);
        GroupDefinitionService = new ChatGroupDefinitionService(_chatServerContext);

        _synchronizer = new ChatSynchronizer(_chatServerContext, GroupDefinitionService);
        MessageService = new ChatMessageService(_chatServerContext,GroupDefinitionService);

    }

    public ChatGroupDefinitionService GroupDefinitionService { get; }

    public string Identity => _chatServerContext.Sender;


    /// <summary>
    /// Retrieves incoming messages and commands
    /// </summary>
    public void SynchronizeData()
    {
        _synchronizer.SynchronizeData();
    }
}