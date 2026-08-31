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

namespace MdcAi.ChatUI.Sessions;

using OpenAiApi;
using ChatCore.Sessions;
using ChatCore.Tools;
using ChatCore.Tools.BuiltIn;

/// <summary>
/// Builds the shared ChatCore services from the app's built-in tools. The registry is
/// immutable and cached; each conversation's session service is small and stateless across
/// turns (DSH proposal §4).
/// </summary>
public static class ConversationSessionServices
{
    private static ChatToolRegistry _registry;

    public static IReadOnlyList<string> BuiltInToolNames { get; } = new[]
    {
        "read_file", "list_dir", "grep", "write_file", "patch_file", "run_powershell",
        "get_job", "stop_job"
    };

    public static ChatToolRegistry Registry =>
        _registry ??= ChatToolRegistry.Build(new IChatTool[]
        {
            new ReadFileChatTool(),
            new ListDirChatTool(),
            new GrepChatTool(),
            new WriteFileChatTool(),
            new PatchFileChatTool(),
            new RunPowerShellChatTool(),
            new GetJobChatTool(),
            new StopJobChatTool()
        });

    public static ChatSessionService Create(IOpenAiApi api) => new(api, Registry);
}