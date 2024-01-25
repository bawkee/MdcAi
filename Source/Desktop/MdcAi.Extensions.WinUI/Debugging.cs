#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.Extensions.WinUI;

public static class Debugging
{
    public static bool Enabled
#if DEBUG        
        = true;
#else
        = false; // Turns everything off (no debugging)
#endif
    public static bool MockMessages = true; // Mocks system messages (doesn't use api)
    public static bool NumberedMessages = true; // If MockMessages true then mocked messages are simple generic numbered messages rather than some md
    public static bool AutoSendFirstMessage = false; // If MockMessages true then automatically send the first user message 
    public static bool AutoSuggestNames = false; // Automatically generate conversation names with gpt

    public static bool MockModels = true; // Don't spam the api, mock models    
    public static bool NpmRenderer = false; // Render messages from the npm start loopback (localhost:3000)

    public static int UserMessageCounter = 0;
    public static int SystemMessageCounter = 0;

    public static bool IsBindingTracingEnabled = false;

    public static bool LogSql = false;
}
