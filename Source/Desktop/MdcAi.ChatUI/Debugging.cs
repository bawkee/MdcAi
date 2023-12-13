namespace MdcAi.ChatUI;

public static class Debugging
{
#if DEBUG
    public static bool Enabled = true; // Turns everything off (no debugging)
    
    public static bool MockMessages = true; // Mocks system messages (doesn't use api)
    public static bool NumberedMessages = false; // If MockMessages true then mocked messages are simple generic numbered messages rather than some md
    public static bool AutoSendFirstMessage = false; // If MockMessages true then automatically send the first user message 
    public static bool AutoSuggestNames = false; // Automatically generate conversation names with gpt

    public static bool MockModels = true; // Don't spam the api, mock models    
    public static bool NpmRenderer = false; // Render messages from the npm start loopback (localhost:3000)

    public static int UserMessageCounter = 0;
    public static int SystemMessageCounter = 0;
#else
    public static bool Enabled = false; // Turns everything off (no debugging)
    
    public static bool MockMessages = true; // Mocks system messages (doesn't use api)
    public static bool NumberedMessages = false; // If MockMessages true then mocked messages are simple generic numbered messages rather than some md
    public static bool AutoSendFirstMessage = false; // If MockMessages true then automatically send the first user message 
    public static bool AutoSuggestNames = false; // Automatically generate conversation names with gpt

    public static bool MockModels = true; // Don't spam the api, mock models    
    public static bool NpmRenderer = false; // Render messages from the npm start loopback (localhost:3000)

    public static int UserMessageCounter = 0;
    public static int SystemMessageCounter = 0;
#endif
}
