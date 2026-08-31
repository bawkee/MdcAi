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

namespace MdcAi.OpenAiApi.IntegrationTests;

using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatCore.Tools.BuiltIn;

/// <summary>
/// P1-12 provider smoke: a real tool-capable reasoning model (DeepSeek over OpenRouter) must
/// read a fixture, call at least one tool, receive its result, and finish WITHOUT a 400
/// reasoning-history error - in streaming mode through the actual step driver, and in
/// non-streaming mode with faithful assistant reasoning/tool replay. Paid; opt-in via the
/// OpenRouter key secret (early-returns when absent).
/// </summary>
public class AgenticToolingSmokeTests : IDisposable
{
    private readonly string _workspace;

    public AgenticToolingSmokeTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "mdcai-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch { /* best-effort */ }
    }

    private static OpenAiClient RouterClient() => new(AiProviders.OpenRouter, new AiProviderCredentials
    {
        ApiKey = TestSecrets.OpenRouterApiKey,
        RefererUrl = "http://localhost:3431/",
        AppTitle = "MDC AI"
    });

    private static bool HasKey => TestSecrets.HasOpenRouterKey;

    private static readonly string DeepSeekModel = "deepseek/deepseek-chat";

    /// <summary>The driver's in-memory transcript, so the smoke runs headlessly like ChatCore tests.</summary>
    private sealed class TranscriptSink : IChatSessionSink
    {
        public List<ChatMessage> Messages { get; } = new();
        public string TurnId { get; } = "smoke-turn";

        public ValueTask<ChatTranscriptSnapshot> GetCurrentBranchAsync(CancellationToken ct) =>
            new(new ChatTranscriptSnapshot(TurnId, Messages.ToList()));

        public ValueTask<string> BeginAssistantAsync(ChatStepInfo step, CancellationToken ct)
        {
            var id = step.TurnId + "-" + step.StepNumber;
            return new ValueTask<string>(id);
        }

        public ValueTask ApplyAssistantDeltaAsync(string messageId, ChatAssistantDelta delta, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask CommitAssistantAsync(string messageId, ChatAssistantRecord record, CancellationToken ct)
        {
            Messages.Add(record.Message);
            return ValueTask.CompletedTask;
        }

        public ValueTask AbandonAssistantAsync(string messageId, bool keepDeliveredPrefix, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask SetModelRequestAttemptAsync(ChatModelRequestAttemptView attempt, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask SetToolStateAsync(ChatToolExecutionView tool, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask AppendToolResultAsync(ChatToolResultRecord result, CancellationToken ct)
        {
            Messages.Add(new ChatMessage(ChatMessageRole.Tool, result.Result.ModelContent)
            {
                ToolCallId = result.ToolCallId
            });
            return ValueTask.CompletedTask;
        }

        public ValueTask CheckpointTurnAsync(ChatTurnCheckpoint checkpoint, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Streaming_deepseek_runs_read_file_turn_end_to_end()
    {
        if (!HasKey) return;
        await File.WriteAllTextAsync(Path.Combine(_workspace, "fixture.txt"), "the-secret-fixture-value-42");

        using var client = RouterClient();
        var registry = ChatToolRegistry.Build(new IChatTool[] { new ReadFileChatTool() });
        var service = new ChatSessionService(client, registry);
        var sink = new TranscriptSink();

        var turn = new ChatTurnRequest(
            "smoke-convo", sink.TurnId, "smoke-trigger",
            AiProviders.OpenRouterKey, DeepSeekModel, null,
            "You are a file reader. Use the read_file tool with path 'fixture.txt', then report the exact secret value you read.",
            _workspace,
            new[] { "read_file" }, ChatTurnOrigin.Human, null, ChatTurnLimits.Default);

        // Seed an explicit user turn so the model sees a direct read request.
        sink.Messages.Add(new ChatMessage(ChatMessageRole.User,
            "Read the file fixture.txt with read_file and tell me the secret value it contains."));

        var result = await service.RunTurnAsync(turn, sink, CancellationToken.None);

        // The turn must finish as Completed - a 400 reasoning-history error would surface as Failed.
        Assert.True(result.IsSuccess, $"TURN FAILED: {result.Outcome} {result.ErrorMessage}");
        Assert.True(result.StepCount >= 2,
                    $"expected a read step + a final answer, got {result.StepCount} steps. " +
                    $"Transcript: {string.Join("\n", sink.Messages.Select(m => $"[{m.Role}] {m.Content ?? "<null>"}"))}");

        // At least one tool result, and the final answer references the fixture content.
        var toolResults = sink.Messages.Where(m => m.Role == ChatMessageRole.Tool).ToArray();
        Assert.True(toolResults.Length >= 1, "the model never called read_file");
        Assert.Contains(toolResults, t => t.ToolCallId != null);

        var final = sink.Messages.Last(m => m.Role == ChatMessageRole.Assistant);
        Assert.NotNull(final.Content);
        Assert.Contains("the-secret-fixture-value-42", final.Content,
                        StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_streaming_deepseek_replays_assistant_tool_history_without_400()
    {
        if (!HasKey) return;
        using var client = RouterClient();

        // First request: tools advertised, no reasoning to replay yet.
        var tool = new ChatTool
        {
            Type = "function",
            Function = new FunctionTool
            {
                Name = "read_file",
                Description = "Reads a workspace file.",
                Parameters = JObject.Parse(
                    """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""")
            }
        };

        var request1 = new ChatRequest
        {
            Model = DeepSeekModel,
            ProviderKey = AiProviders.OpenRouterKey,
            NumChoicesPerMessage = 1,
            MaxTokens = 300,
            Messages = new[]
            {
                new ChatMessage(ChatMessageRole.System, "You are a terse assistant. Use the read_file tool when asked to inspect a file."),
                new ChatMessage(ChatMessageRole.User, "Inspect the file at src/Important.cs and summarize what it does.")
            },
            Tools = new[] { tool }
        };

        var response1 = await client.CreateChatCompletions(request1);

        Assert.NotEmpty(response1.Choices);
        var assistant1 = response1.Choices[0].Message;
        Assert.NotEmpty(assistant1.ToolCalls ?? Array.Empty<ChatMessageToolCall>());

        // Second request: replay the assistant message EXACTLY (content, reasoning +
        // reasoning_details, tool_calls) plus a tool result. DeepSeek thinking mode REQUIRES the
        // prior reasoning when tools are present - a lossy replay returns HTTP 400.
        var messages = new List<ChatMessage>(request1.Messages)
        {
            new(assistant1),
            new ChatMessage(ChatMessageRole.Tool, """{"ok":true,"content":"It is a config loader.\n"}""")
            {
                ToolCallId = assistant1.ToolCalls[0].Id
            }
        };

        var request2 = new ChatRequest
        {
            Model = DeepSeekModel,
            ProviderKey = AiProviders.OpenRouterKey,
            NumChoicesPerMessage = 1,
            MaxTokens = 300,
            Messages = messages,
            Tools = new[] { tool }
        };

        var response2 = await client.CreateChatCompletions(request2);

        // No 400: the continuation completed with a final answer.
        Assert.NotEmpty(response2.Choices);
        Assert.NotNull(response2.Choices[0].Message.Content);
        Assert.True(response2.Choices[0].Message.Content.Length > 0);
    }
}