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

using ChatCore.Helpers;
using ChatCore.Sessions;
using ChatCore.Tools;
using ChatCore.Tools.BuiltIn;
using OpenAiApi;

/// <summary>
/// Builds the shared ChatCore services from the app's built-in tools. The plain tool registry is
/// immutable and cached; the api-bound registry (delegate_task helper included) is built per
/// conversation because it needs the live IOpenAiApi.
/// </summary>
public static class ConversationSessionServices
{
    private static ChatToolRegistry _registry;

    public static IReadOnlyList<string> BuiltInToolNames { get; } = new[]
    {
        "read_file", "list_dir", "grep", "write_file", "patch_file", "run_powershell",
        "get_job", "stop_job", "delegate_task"
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

    /// <summary>
    /// Api-bound registry: adds delegate_task whose one-shot helpers share this conversation's
    /// IOpenAiApi. Per-conversation instance (the helper closes over the live client).
    /// </summary>
    public static ChatToolRegistry BuildApiBoundRegistry(IOpenAiApi api)
    {
        var helpers = new HelperSessionService(api, Registry);
        return ChatToolRegistry.Build(Registry.All.Append(new DelegateTaskChatTool(helpers)));
    }

    public static ChatSessionService Create(IOpenAiApi api) => new(api, BuildApiBoundRegistry(api));
}