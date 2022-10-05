using System;
using System.Collections.Generic;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatData
{
    public List<ChatMessage> Messages { get; } = new List<ChatMessage>();
    public List<ChatGroup> Groups { get; } = new List<ChatGroup>();
}

public class ChatApp
{
    private readonly ChatServerContext _chatServerContext;
    private readonly ChatData _chatData;
    private readonly ChatSynchronizer _synchronizer;
    private readonly ConversationService _conversationService;
    private readonly ConversationDefinitionService _conversationDefinitionService;


    private string _latestCursor = "";
    public ChatMessageService MessageService { get; }

    public ChatApp(TestSampleAppContext appContext, WebScaffold scaffold)
    {
        _chatServerContext = new ChatServerContext(appContext, scaffold);
        _conversationDefinitionService = new ConversationDefinitionService(_chatServerContext);
        _conversationService = new ConversationService(_chatServerContext);
        _synchronizer = new ChatSynchronizer(_chatServerContext, _conversationDefinitionService, _conversationService);
        MessageService = new ChatMessageService(_chatServerContext, _conversationDefinitionService, _conversationService);
    }


    public string Identity => _chatServerContext.Sender;
    public ConversationDefinitionService ConversationDefinitionService => _conversationDefinitionService;

    /// <summary>
    /// Retrieves incoming messages and commands
    /// </summary>
    public void SynchronizeData()
    {
        _synchronizer.SynchronizeData();
    }
}