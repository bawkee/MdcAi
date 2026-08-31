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

namespace MdcAi.OpenAiApi.Tests;

using Newtonsoft.Json.Linq;

/// <summary>
/// Golden protocol-fidelity tests for the Phase 1A wire repair: exact assistant
/// content + reasoning + reasoning_details + tool_calls replay, explicit null content
/// presence, deep copies, indexed streaming tool calls, rich JSON Schema, and preserved
/// unknown fields. See DSH_IMPLEMENTATION_PROPOSAL.md §6.1 / §9.1.
/// </summary>
public class WireFidelityTests
{
    /// <summary>The exact serializer settings the transport uses for request bodies.</summary>
    private static readonly JsonSerializerSettings RequestSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    private static string SerializeRequest(ChatRequest request) =>
        JsonConvert.SerializeObject(request, RequestSettings);

    [Fact]
    public void Assistant_tool_call_message_keeps_explicit_null_content_on_request()
    {
        // DeepSeek/OpenRouter distinguish content:null from an absent property during tool
        // continuation; the request serializer ignores nulls globally, so content opts back in.
        var request = new ChatRequest
        {
            Model = "deepseek/deepseek-chat",
            Messages = new[]
            {
                new ChatMessage(ChatMessageRole.User, "read the file"),
                new ChatMessage(ChatMessageRole.Assistant)
                {
                    Content = null,
                    ReasoningContent = "I should read the file first.",
                    ToolCalls = new[]
                    {
                        new ChatMessageToolCall
                        {
                            Id = "call_1",
                            Type = "function",
                            Function = new ChatMessageFunction { Name = "read_file", Arguments = """{"path":"a.txt"}""" }
                        }
                    }
                }
            },
            Tools = new[] { new ChatTool { Type = "function", Function = new FunctionTool { Name = "read_file" } } }
        };

        var json = SerializeRequest(request);

        Assert.Contains("\"content\":null", json);
        Assert.Contains("\"reasoning_content\":\"I should read the file first.\"", json);
        Assert.Contains("\"tool_calls\":", json);
        Assert.Contains("\"id\":\"call_1\"", json);
        Assert.Contains("\"arguments\":\"{\\\"path\\\":\\\"a.txt\\\"}\"", json);
    }

    [Fact]
    public void Copy_constructor_deep_copies_tool_calls_and_reasoning()
    {
        var original = new ChatMessage(ChatMessageRole.Assistant)
        {
            Content = null,
            ReasoningContent = "thinking",
            ReasoningRaw = JToken.Parse("""{"summary":["a","b"]}"""),
            ReasoningDetails = JArray.Parse(
                """[{"type":"reasoning","content":[{"type":"text","text":"line1"}],"signature":"sig-abc"}]"""),
            ToolCalls = new[]
            {
                new ChatMessageToolCall
                {
                    Id = "call_1",
                    Type = "function",
                    Index = 0,
                    Function = new ChatMessageFunction { Name = "read_file", Arguments = "{}" }
                },
                new ChatMessageToolCall
                {
                    Id = "call_2",
                    Type = "function",
                    Index = 1,
                    Function = new ChatMessageFunction { Name = "list_dir", Arguments = "{}" }
                }
            },
            ExtensionData = new Dictionary<string, JToken> { ["logprobs"] = JToken.Parse("""{"x":1}""") }
        };

        var copy = new ChatMessage(original);

        Assert.Equal(original.Role, copy.Role);
        Assert.Equal(original.Content, copy.Content);
        Assert.Equal(original.ReasoningContent, copy.ReasoningContent);
        Assert.Equal(original.ReasoningRaw.ToString(Formatting.None), copy.ReasoningRaw.ToString(Formatting.None));
        Assert.Equal(original.ReasoningDetails.ToString(Formatting.None), copy.ReasoningDetails.ToString(Formatting.None));

        // Deep copies: distinct containers, equal content.
        Assert.NotSame(original.ToolCalls, copy.ToolCalls);
        Assert.Equal(2, copy.ToolCalls.Length);
        Assert.NotSame(original.ToolCalls[0], copy.ToolCalls[0]);
        Assert.NotSame(original.ToolCalls[0].Function, copy.ToolCalls[0].Function);
        Assert.Equal("call_1", copy.ToolCalls[0].Id);
        Assert.Equal("call_2", copy.ToolCalls[1].Id);
        Assert.Equal("read_file", copy.ToolCalls[0].Function.Name);
        Assert.Equal(0, copy.ToolCalls[0].Index);
        Assert.Equal(1, copy.ToolCalls[1].Index);

        Assert.Equal("sig-abc", (string)copy.ReasoningDetails[0]["signature"]);
        Assert.Equal("{\"x\":1}", copy.ExtensionData["logprobs"].ToString(Formatting.None));

        // Mutating the copy must not touch the original.
        copy.ToolCalls[0].Function.Name = "changed";
        Assert.Equal("read_file", original.ToolCalls[0].Function.Name);
        copy.ReasoningDetails[0]["signature"] = "tampered";
        Assert.Equal("sig-abc", (string)original.ReasoningDetails[0]["signature"]);
    }

    [Fact]
    public void Raw_reasoning_string_round_trips_exactly()
    {
        var message = JsonConvert.DeserializeObject<ChatMessage>(
            """{"role":"assistant","content":"","reasoning":"incremental think","reasoning_details":[{"type":"reasoning","content":[{"type":"text","text":"t"}]}]}""");

        Assert.IsType<JValue>(message.ReasoningRaw);
        Assert.Equal("incremental think", (string)message.ReasoningRaw);
        Assert.Equal("incremental think", message.ReasoningText);

        var request = new ChatRequest
        {
            Model = "anthropic/claude-3.5-sonnet",
            Messages = new[] { message }
        };

        var json = SerializeRequest(request);
        Assert.Contains("\"reasoning\":\"incremental think\"", json);
        Assert.Contains("\"reasoning_details\":[{\"type\":\"reasoning\"", json);
    }

    [Fact]
    public void Signed_reasoning_details_keep_order_and_signature_fields()
    {
        var details = JArray.Parse(
            """
            [
              {"type":"reasoning","summary":[{"type":"text","text":"first"}],"content":[],"signature":"S1"},
              {"type":"reasoning","summary":[],"content":[{"type":"text","text":"second"}],"signature":"S2"}
            ]
            """);

        var message = new ChatMessage(ChatMessageRole.Assistant)
        {
            ReasoningDetails = details
        };

        var roundTripped = JsonConvert.DeserializeObject<ChatMessage>(
            JsonConvert.SerializeObject(message, RequestSettings));

        Assert.Equal(2, roundTripped.ReasoningDetails.Count);
        Assert.Equal("S1", (string)roundTripped.ReasoningDetails[0]["signature"]);
        Assert.Equal("S2", (string)roundTripped.ReasoningDetails[1]["signature"]);
        Assert.Equal("first", roundTripped.ReasoningDetails[0]["summary"][0]["text"]);
    }

    [Fact]
    public void Streaming_tool_call_deltas_carry_index_and_accumulate_identities()
    {
        // Two interleaved indexed tool calls across chunks - the assembler keys on index, not
        // on current chunk array position.
        var chunks = new[]
        {
            """{"id":"a","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}}]}}]}""",
            """{"id":"a","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"id":"call_2","type":"function","function":{"name":"list_dir","arguments":""}}]}}]}""",
            """{"id":"a","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":"}}]}}]}""",
            """{"id":"a","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"a.txt\"}"}},{"index":1,"function":{"arguments":"{}"}}]}}]}"""
        };

        var parsed = chunks.Select(c => JsonConvert.DeserializeObject<ChatResult>(c)).ToArray();

        var byIndex = new Dictionary<int, ChatMessageToolCall>();
        foreach (var chunk in parsed)
        foreach (var deltaCall in chunk.Choices[0].Delta.ToolCalls)
        {
            var index = deltaCall.Index ?? byIndex.Count;
            if (!byIndex.TryGetValue(index, out var existing))
                byIndex[index] = new ChatMessageToolCall(deltaCall);
            else
            {
                existing.Id ??= deltaCall.Id;
                existing.Type ??= deltaCall.Type;
                existing.Function ??= new ChatMessageFunction();
                existing.Function.Name ??= deltaCall.Function?.Name;
                existing.Function.Arguments += deltaCall.Function?.Arguments;
            }
        }

        Assert.Equal(2, byIndex.Count);
        Assert.Equal("call_1", byIndex[0].Id);
        Assert.Equal("read_file", byIndex[0].Function.Name);
        Assert.Equal("""{"path":"a.txt"}""", byIndex[0].Function.Arguments);
        Assert.Equal("call_2", byIndex[1].Id);
        Assert.Equal("{}", byIndex[1].Function.Arguments);
    }

    [Fact]
    public void Unknown_optional_fields_are_preserved_across_round_trip()
    {
        var message = JsonConvert.DeserializeObject<ChatMessage>(
            """{"role":"assistant","content":"hi","custom_future_field":{"nested":[1,2,3]}}""");

        Assert.NotNull(message.ExtensionData);
        Assert.True(message.ExtensionData.ContainsKey("custom_future_field"));

        var request = new ChatRequest { Model = "gpt-4o", Messages = new[] { message } };
        var json = SerializeRequest(request);

        Assert.Contains("\"custom_future_field\":{\"nested\":[1,2,3]}", json);
    }

    [Fact]
    public void ChatRequest_copy_carries_provider_and_adapter_fields()
    {
        var original = new ChatRequest
        {
            Model = "anthropic/claude-3.5-sonnet",
            ProviderKey = "openrouter",
            ReasoningOptions = JObject.Parse("""{"effort":"high","max_tokens":8000}"""),
            ToolChoice = JToken.Parse("""{"type":"function","function":{"name":"read_file"}}"""),
            ParallelToolCalls = false,
            Messages = new[]
            {
                new ChatMessage(ChatMessageRole.User, "hi")
                {
                    ReasoningDetails = JArray.Parse("""[{"type":"reasoning"}]""")
                }
            }
        };

        var copy = new ChatRequest(original);

        Assert.Equal("openrouter", copy.ProviderKey);
        Assert.Equal("high", (string)copy.ReasoningOptions["effort"]);
        Assert.Equal("read_file", (string)copy.ToolChoice["function"]["name"]);
        Assert.False(copy.ParallelToolCalls);
        // Messages deep-copied with reasoning.
        Assert.NotSame(original.Messages, copy.Messages);
        Assert.NotSame(original.Messages[0], copy.Messages[0]);
        Assert.Contains("\"reasoning_details\"", SerializeRequest(copy));

        // Provider/logic fields must never serialize into the wire body.
        var json = SerializeRequest(original);
        Assert.DoesNotContain("ProviderKey", json);
        Assert.Contains("\"tool_choice\":", json);
        Assert.Contains("\"parallel_tool_calls\":false", json);
    }

    [Fact]
    public void Rich_tool_schema_and_strict_serialize_as_full_json_schema()
    {
        var parameters = JObject.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "path": { "type": "string", "description": "workspace-relative path" },
                "line_count": { "type": "integer", "minimum": 1, "maximum": 500 },
                "tags": { "type": "array", "items": { "type": "string", "enum": ["a", "b"] } },
                "meta": { "type": "object", "properties": { "depth": { "type": "integer" } } }
              },
              "required": ["path"]
            }
            """);

        var request = new ChatRequest
        {
            Model = "deepseek/deepseek-chat",
            Messages = new[] { new ChatMessage(ChatMessageRole.User, "help") },
            Tools = new[]
            {
                new ChatTool
                {
                    Type = "function",
                    Function = new FunctionTool
                    {
                        Name = "read_file",
                        Description = "Read a file.",
                        Parameters = parameters,
                        Strict = true
                    }
                }
            }
        };

        var json = SerializeRequest(request);

        Assert.Contains("\"additionalProperties\":false", json);
        Assert.Contains("\"enum\":[\"a\",\"b\"]", json);
        Assert.Contains("\"maximum\":500", json);
        Assert.Contains("\"minimum\":1", json);
        Assert.Contains("\"strict\":true", json);
    }

    [Fact]
    public void Legacy_FunctionToolParams_converts_to_object_schema()
    {
        var legacy = new FunctionToolParams
        {
            Type = "object",
            Properties = new Dictionary<string, FunctionToolParamProperty>
            {
                ["path"] = new() { Type = "string", Description = "the path" }
            },
            Required = new[] { "path" }
        };

        var tool = FunctionTool.FromLegacy("read_file", "Reads", legacy);

        Assert.Equal("object", (string)tool.Parameters["type"]);
        Assert.Equal("string", (string)tool.Parameters["properties"]["path"]["type"]);
        Assert.Equal("the path", (string)tool.Parameters["properties"]["path"]["description"]);
        Assert.Equal("path", (string)tool.Parameters["required"][0]);
    }

    [Fact]
    public void SupportsTools_derives_from_provider_metadata_only()
    {
        var withTools = new AiModel("anthropic/claude-3.5-sonnet")
        {
            SupportedParameters = new[] { "tools", "reasoning" }
        };
        var withoutTools = new AiModel("some/embedding-model")
        {
            SupportedParameters = new[] { "input" }
        };
        var unknown = new AiModel("gpt-4o");

        Assert.True(withTools.SupportsTools);
        Assert.False(withoutTools.SupportsTools);
        Assert.Null(unknown.SupportsTools);

        // Wire round trip keeps the metadata.
        var parsed = JsonConvert.DeserializeObject<AiModel>(
            JsonConvert.SerializeObject(withTools));
        Assert.True(parsed.SupportsTools);
    }
}