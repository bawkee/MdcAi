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

namespace MdcAi.ChatCore.Tests;

using MdcAi.ChatCore.Sessions;
using MdcAi.OpenAiApi;
using Newtonsoft.Json.Linq;

public class ChatResponseAssemblerTests
{
    private static ChatResponseAssembler Assemble(params ChatResult[] chunks)
    {
        var assembler = new ChatResponseAssembler();
        foreach (var chunk in chunks)
            assembler.Accept(chunk);
        assembler.Seal();
        return assembler;
    }

    [Fact]
    public void Appends_content_across_chunks()
    {
        var a = Assemble(FakeChunks.RoleChunk("assistant"),
                         FakeChunks.Content("Hel"),
                         FakeChunks.Content("lo"),
                         FakeChunks.Finish());

        Assert.Equal("Hello", a.Content);
        Assert.True(a.HasAcceptedDelta);
        Assert.False(a.IsMaxTokens);
    }

    [Fact]
    public void Appends_reasoning_content_independently_of_content()
    {
        var a = Assemble(FakeChunks.Reasoning("think "),
                         FakeChunks.Reasoning("more\n"),
                         FakeChunks.Content("answer"),
                         FakeChunks.Finish());

        Assert.Equal("think more\n", a.ReasoningContent);
        Assert.Equal("answer", a.Content);
    }

    [Fact]
    public void Preserves_raw_reasoning_string_and_structured_details_in_order()
    {
        var a = Assemble(
            new ChatResult
            {
                Id = "r",
                Choices = new[]
                {
                    new ChatChoice
                    {
                        Delta = new ChatMessage(ChatMessageRole.Assistant)
                        {
                            ReasoningRaw = JToken.Parse("\"incr\"")
                        }
                    }
                }
            },
            new ChatResult
            {
                Id = "r",
                Choices = new[]
                {
                    new ChatChoice
                    {
                        Delta = new ChatMessage(ChatMessageRole.Assistant)
                        {
                            ReasoningRaw = JToken.Parse("\"emental\""),
                            ReasoningDetails = JArray.Parse(
                                """[{"type":"reasoning","content":[{"type":"text","text":"b1"}],"signature":"S1"}]""")
                        }
                    }
                }
            },
            new ChatResult
            {
                Id = "r",
                Choices = new[]
                {
                    new ChatChoice
                    {
                        Delta = new ChatMessage(ChatMessageRole.Assistant)
                        {
                            ReasoningDetails = JArray.Parse(
                                """[{"type":"reasoning","content":[{"type":"text","text":"b2"}],"signature":"S2"}]""")
                        }
                    }
                }
            });

        Assert.Equal("incremental", (string)a.ReasoningRaw);
        Assert.Equal(2, a.ReasoningDetails.Count);
        Assert.Equal("S1", (string)a.ReasoningDetails[0]["signature"]);
        Assert.Equal("S2", (string)a.ReasoningDetails[1]["signature"]);
    }

    [Fact]
    public void Accumulates_fragmented_single_tool_call_by_index()
    {
        var a = Assemble(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: ""),
            FakeChunks.ToolCallChunk(0, args: "{\"path\":"),
            FakeChunks.ToolCallChunk(0, args: "\"a.txt\"}"),
            FakeChunks.Finish("tool_calls"));

        var call = Assert.Single(a.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("read_file", call.Function.Name);
        Assert.Equal("""{"path":"a.txt"}""", call.Function.Arguments);
        Assert.True(a.HasCompleteToolArguments);
        Assert.True(a.HasAcceptedDelta);
    }

    [Fact]
    public void Accumulates_two_interleaved_indexed_calls_in_model_order()
    {
        var a = Assemble(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: ""),
            FakeChunks.ToolCallChunk(1, id: "call_2", name: "list_dir", args: ""),
            FakeChunks.ToolCallChunk(1, args: "{}"),
            FakeChunks.ToolCallChunk(0, args: "{\"path\":\"x\"}"),
            FakeChunks.Finish("tool_calls"));

        Assert.Equal(2, a.ToolCalls.Count);
        Assert.Equal("call_1", a.ToolCalls[0].Id);
        Assert.Equal("call_2", a.ToolCalls[1].Id);
        Assert.Equal("""{"path":"x"}""", a.ToolCalls[0].Function.Arguments);
        Assert.Equal("{}", a.ToolCalls[1].Function.Arguments);
    }

    [Fact]
    public void Missing_id_or_name_marks_tool_calls_incomplete()
    {
        var a = Assemble(
            FakeChunks.ToolCallChunk(0, name: "read_file", args: "{}"),
            FakeChunks.Finish("tool_calls"));

        Assert.Single(a.ToolCalls);
        Assert.Null(a.ToolCalls[0].Id);
        Assert.False(a.HasCompleteToolArguments);
    }

    [Fact]
    public void Malformed_json_arguments_marks_tool_calls_incomplete()
    {
        var a = Assemble(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{\"path\":"),
            FakeChunks.Finish("tool_calls"));

        Assert.False(a.HasCompleteToolArguments);
    }

    [Fact]
    public void Finish_reason_length_is_sticky_max_tokens()
    {
        var a = Assemble(FakeChunks.Content("partial"), FakeChunks.Finish("length"));

        Assert.True(a.IsMaxTokens);
        Assert.Equal("length", a.FinishReason);
    }

    [Fact]
    public void Usage_only_empty_choices_chunk_is_retained()
    {
        var a = Assemble(FakeChunks.Content("hi"), FakeChunks.UsageOnly(10, 5), FakeChunks.Finish());

        Assert.NotNull(a.Usage);
        Assert.Equal(15, a.Usage.TotalTokens);
        Assert.Equal(10, a.Usage.PromptTokens);
        Assert.Equal("hi", a.Content);
    }

    [Fact]
    public void Pure_tool_call_step_has_null_content()
    {
        var a = Assemble(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{}"),
            FakeChunks.Finish("tool_calls"));

        var message = a.BuildAssistantMessage();
        Assert.Null(message.Content);
        Assert.Single(message.ToolCalls);
        Assert.Equal(ChatMessageRole.Assistant, message.Role);
    }

    [Fact]
    public void Reasoning_plus_tool_calls_on_one_message_are_kept_together()
    {
        var a = Assemble(
            FakeChunks.Reasoning("I'll read it."),
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{}"),
            FakeChunks.Finish("tool_calls"));

        var message = a.BuildAssistantMessage();
        Assert.Equal("I'll read it.", message.ReasoningContent);
        Assert.Single(message.ToolCalls);
    }

    [Fact]
    public void BuildCurrentDelta_matches_final_message()
    {
        var a = Assemble(
            FakeChunks.Content("abc"),
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{}"),
            FakeChunks.Finish("tool_calls"));

        var delta = a.BuildCurrentDelta();
        Assert.Equal("abc", delta.Content);
        Assert.Single(delta.ToolCallDeltas);

        var built = a.BuildAssistantMessage();
        Assert.Equal(built.Content, delta.Content);
        Assert.Equal(built.ToolCalls[0].Id, delta.ToolCallDeltas[0].Id);
    }
}