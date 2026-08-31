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

using MdcAi.ChatCore.Tools;

public class ChatToolRegistryTests
{
    [Fact]
    public void Build_fails_fast_on_duplicate_names()
    {
        var a = new FakeReadTool();
        var b = new FakeReadTool();

        Assert.Throws<InvalidOperationException>(() => ChatToolRegistry.Build(new IChatTool[] { a, b }));
    }

    [Fact]
    public void Names_are_case_sensitive_on_the_wire()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeReadTool() });

        Assert.True(registry.TryGet("read_file", out _));
        Assert.False(registry.TryGet("READ_FILE", out _));
    }

    [Fact]
    public void Filtered_registry_only_exposes_allowed_names()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeReadTool() });

        var filtered = registry.Filtered(new[] { "read_file" });
        Assert.Single(filtered.All);

        var empty = registry.Filtered(Array.Empty<string>());
        Assert.Empty(empty.All);
    }

    [Fact]
    public void ToWireTools_advertises_only_enabled_names_with_full_schema()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeReadTool() });

        var wire = registry.ToWireTools(new[] { "read_file" });

        var tool = Assert.Single(wire);
        Assert.Equal("function", tool.Type);
        Assert.Equal("read_file", tool.Function.Name);
        Assert.Equal("object", (string)tool.Function.Parameters["type"]);
        Assert.Equal("path", (string)tool.Function.Parameters["required"][0]);
    }

    [Fact]
    public void ToWireTools_with_no_enabled_names_sends_nothing()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeReadTool() });
        Assert.Empty(registry.ToWireTools(Array.Empty<string>()));
    }
}

public class ChatToolArgumentValidatorTests
{
    private static readonly JObject ReadSchema = JObject.Parse(
        """{"type":"object","additionalProperties":false,"properties":{"path":{"type":"string"},"max_chars":{"type":"integer","minimum":1,"maximum":4096},"kind":{"type":"string","enum":["text","binary"]}},"required":["path"]}""");

    private static ChatToolArgumentValidator.ChatArgumentValidation Validate(string json) =>
        ChatToolArgumentValidator.Instance.Validate(JObject.Parse(json), ReadSchema);

    [Fact]
    public void Accepts_valid_arguments()
    {
        Assert.True(Validate("""{"path":"a.txt","max_chars":100}""").IsValid);
        Assert.True(Validate("""{"path":"a.txt"}""").IsValid);
    }

    [Fact]
    public void Rejects_missing_required()
    {
        var r = Validate("""{"max_chars":1}""");
        Assert.False(r.IsValid);
        Assert.Equal("missing_required", r.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_property_when_additionalProperties_false()
    {
        var r = Validate("""{"path":"a.txt","sneaky":"x"}""");
        Assert.False(r.IsValid);
        Assert.Equal("unknown_property", r.ErrorCode);
    }

    [Fact]
    public void Rejects_wrong_type_and_out_of_range()
    {
        Assert.Equal("type_mismatch", Validate("""{"path":42}""").ErrorCode);
        Assert.Equal("below_minimum", Validate("""{"path":"a","max_chars":0}""").ErrorCode);
        Assert.Equal("above_maximum", Validate("""{"path":"a","max_chars":99999}""").ErrorCode);
        Assert.Equal("not_in_enum", Validate("""{"path":"a","kind":"weird"}""").ErrorCode);
    }

    [Fact]
    public void Rejects_non_object_arguments()
    {
        var r = ChatToolArgumentValidator.Instance.Validate(JToken.Parse("[1,2]"), ReadSchema);
        Assert.False(r.IsValid);
        Assert.Equal("type_mismatch", r.ErrorCode);
    }

    [Fact]
    public void Nested_object_and_array_schemas_validate()
    {
        var schema = JObject.Parse(
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"n":{"type":"integer"}},"required":["n"]}}},"required":["items"]}""");

        var ok = ChatToolArgumentValidator.Instance.Validate(
            JObject.Parse("""{"items":[{"n":1},{"n":2}]}"""), schema);
        Assert.True(ok.IsValid);

        var bad = ChatToolArgumentValidator.Instance.Validate(
            JObject.Parse("""{"items":[{"n":"x"}]}"""), schema);
        Assert.False(bad.IsValid);
        Assert.Equal("type_mismatch", bad.ErrorCode);
    }
}