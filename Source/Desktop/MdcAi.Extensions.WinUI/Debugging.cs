namespace MdcAi.Extensions.WinUI;

public static class Debugging
{
    public static bool Enabled
#if DEBUG        
        = true; // Turns everything off (no debugging)
#else
        = false;
#endif
    public static bool MockMessages = true; // Mocks system messages (doesn't use api)
    public static bool NumberedMessages = false; // If MockMessages true then mocked messages are simple generic numbered messages rather than some md
    public static bool AutoSendFirstMessage = false; // If MockMessages true then automatically send the first user message 
    public static bool AutoSuggestNames = false; // Automatically generate conversation names with gpt

    public static bool MockModels = true; // Don't spam the api, mock models    
    public static bool NpmRenderer = false; // Render messages from the npm start loopback (localhost:3000)

    public static int UserMessageCounter = 0;
    public static int SystemMessageCounter = 0;

    public static bool IsBindingTracingEnabled = false;

    public static bool LogSql = false;
}
