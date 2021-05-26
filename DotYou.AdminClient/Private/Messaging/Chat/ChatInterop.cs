using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DotYou.AdminClient.Private.Messaging.Chat
{
    public static class ChatInterop
    {
        internal static ValueTask<object> AnchorScrollAtBottom(IJSRuntime jsRuntime, ElementReference element)
        {
            return jsRuntime.InvokeAsync<object>("YFChatFunctions.anchorScrollAtBottom", element);
        }

        internal static ValueTask Focus(IJSRuntime jsRuntime, ElementReference element)
        {
            return jsRuntime.InvokeVoidAsync("YFChatFunctions.focusElement", element);
        }
    }
}
