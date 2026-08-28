// Mock data mirroring the exact wire shape C# sends through PostWebMessageAsJson
// (WebViewSetMessagesRequestDto { Messages: WebViewChatMessageDto[] }), including
// the new Model/Provider fields. Used by the Jest tests and handy for manual poking.

export const mockMessages = [
    {
        Id: "user-1",
        Role: "user",
        Content: "<p>Explain the bug in your author labels.</p>",
        Version: 1,
        VersionCount: 1,
        CreatedTs: "2023-05-26T00:08:00",
        Model: null,
        Provider: null
    },
    {
        Id: "ai-1",
        Role: "assistant",
        Content: "<p>Every assistant message was labelled &quot;You&quot;. Here's the fix.</p>",
        Version: 1,
        VersionCount: 1,
        CreatedTs: "2023-05-26T00:08:05",
        Model: "gpt-4o",
        Provider: "OpenAI"
    },
    {
        Id: "user-2",
        Role: "user",
        Content: "<p>Who is answering now?</p>",
        Version: 1,
        VersionCount: 1,
        CreatedTs: "2023-05-26T00:09:00",
        Model: null,
        Provider: null
    },
    {
        Id: "ai-2",
        Role: "assistant",
        Content: "<p>An OpenRouter-routed model, this time.</p>",
        Version: 2,
        VersionCount: 2,
        CreatedTs: "2023-05-26T00:09:10",
        Model: "anthropic/claude-3-5-sonnet",
        Provider: "OpenRouter"
    },
    {
        Id: "ai-3",
        Role: "assistant",
        Content: "<p>Payloads without a provider stamp should still render.</p>",
        Version: 1,
        VersionCount: 1,
        CreatedTs: "2023-05-26T00:10:00",
        Model: "deepseek/deepseek-chat",
        Provider: null
    },
    {
        Id: "legacy-1",
        Role: "system",
        Content: "<p>An old message from before assistant roles existed.</p>",
        Version: 1,
        VersionCount: 1,
        CreatedTs: "2020-06-20T08:27:47",
        Model: null,
        Provider: null
    }
];

export const setMessagesPayload = (messages = mockMessages) => ({
    Name: "SetMessages",
    Data: { Messages: messages }
});

export const selectionPayload = (index) => ({
    Name: "SetSelection",
    Data: index
});

export const hideCaretPayload = () => ({ Name: "HideCaret" });