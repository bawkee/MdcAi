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

namespace MdcAi.ChatCore.Tools;

using Newtonsoft.Json.Linq;

/// <summary>
/// A pragmatic JSON-Schema subset validator used as HOST validation (the proposal does not trust
/// provider "strict mode" to replace it). Covers object/array/string/integer/number/boolean,
/// required, additionalProperties:false, properties, items, enums, min/max and length bounds.
/// Tool-specific DTO deserialization remains layered on top for the built-in tools.
/// </summary>
public sealed class ChatToolArgumentValidator
{
    public static ChatToolArgumentValidator Instance { get; } = new();

    public sealed record ChatArgumentValidation(bool IsValid, string ErrorCode, string Error)
    {
        public static ChatArgumentValidation Valid { get; } = new(true, null, null);
    }

    public ChatArgumentValidation Validate(JToken arguments, JToken schema)
    {
        if (schema == null || schema.Type == JTokenType.Null)
            return ChatArgumentValidation.Valid;

        if (arguments == null || arguments.Type == JTokenType.Null)
            return new(false, "arguments_required", "Tool call arguments are missing.");

        return ValidateValue(arguments, schema, "$");
    }

    private static ChatArgumentValidation ValidateValue(JToken value, JToken schema, string path)
    {
        // "type" may be a single string or (rarely) an array of acceptable types.
        var types = schema["type"] is { } t
                        ? (t.Type == JTokenType.Array
                               ? t.Values<string>().ToArray()
                               : new[] { t.Value<string>() })
                        : Array.Empty<string>();

        if (types.Length > 0)
        {
            var ok = false;
            foreach (var ty in types)
            {
                if (MatchesType(value, ty)) { ok = true; break; }
            }

            if (!ok)
                return new(false, "type_mismatch",
                           $"{path}: expected {string.Join("|", types)} but got {Describe(value)}.");
        }

        var valueType = value.Type;

        if (valueType == JTokenType.Object)
        {
            var obj = (JObject)value;

            if (schema["required"] is JArray required)
            {
                foreach (var name in required)
                {
                    var key = name.Value<string>();
                    if (obj[key] == null || obj[key].Type == JTokenType.Null)
                        return new(false, "missing_required",
                                   $"{path}: missing required property '{key}'.");
                }
            }

            if (schema["properties"] is JObject props)
            {
                foreach (var prop in props.Properties())
                {
                    if (obj[prop.Name] is { } propValue && propValue.Type != JTokenType.Null)
                    {
                        var sub = ValidateValue(propValue, prop.Value, path + "." + prop.Name);
                        if (!sub.IsValid)
                            return sub;
                    }
                }
            }

            if (schema["additionalProperties"] is { } ap && ap.Type == JTokenType.Boolean && !ap.Value<bool>())
            {
                var allowed = schema["properties"] is JObject p ? p.Properties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
                var unknown = obj.Properties().Select(x => x.Name).FirstOrDefault(n => !allowed.Contains(n));
                if (unknown != null)
                    return new(false, "unknown_property", $"{path}: property '{unknown}' is not allowed.");
            }
        }
        else if (valueType == JTokenType.Array)
        {
            var arr = (JArray)value;
            if (schema["items"] is { } items)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    var sub = ValidateValue(arr[i], items, path + $"[{i}]");
                    if (!sub.IsValid)
                        return sub;
                }
            }

            if (schema["maxItems"] is { } maxItems && arr.Count > maxItems.Value<int>())
                return new(false, "too_many_items", $"{path}: expected at most {maxItems.Value<int>()} items.");
        }
        else if (valueType == JTokenType.String)
        {
            var s = value.Value<string>();
            if (schema["enum"] is JArray enums && !enums.Any(e => string.Equals(e.Value<string>(), s, StringComparison.Ordinal)))
                return new(false, "not_in_enum", $"{path}: value is not one of the allowed options.");
            if (schema["minLength"] is { } minLen && s.Length < minLen.Value<int>())
                return new(false, "too_short", $"{path}: must be at least {minLen.Value<int>()} characters.");
            if (schema["maxLength"] is { } maxLen && s.Length > maxLen.Value<int>())
                return new(false, "too_long", $"{path}: must be at most {maxLen.Value<int>()} characters.");
        }
        else if (valueType is JTokenType.Integer or JTokenType.Float)
        {
            var n = value.Value<decimal>();
            if (schema["minimum"] is { } min && n < min.Value<decimal>())
                return new(false, "below_minimum", $"{path}: must be >= {min.Value<decimal>()}.");
            if (schema["maximum"] is { } max && n > max.Value<decimal>())
                return new(false, "above_maximum", $"{path}: must be <= {max.Value<decimal>()}.");
        }

        return ChatArgumentValidation.Valid;
    }

    private static bool MatchesType(JToken value, string type)
    {
        switch (type)
        {
            case "object":
                return value.Type == JTokenType.Object;
            case "array":
                return value.Type == JTokenType.Array;
            case "string":
                return value.Type == JTokenType.String;
            case "integer":
                return value.Type == JTokenType.Integer;
            case "number":
                return value.Type is JTokenType.Integer or JTokenType.Float;
            case "boolean":
                return value.Type == JTokenType.Boolean;
            case "null":
                return value.Type == JTokenType.Null;
            default:
                return true; // unknown type keyword: be permissive, host tool does its own checks
        }
    }

    private static string Describe(JToken value) =>
        value.Type switch
        {
            JTokenType.String => $"a string",
            JTokenType.Object => "an object",
            JTokenType.Array => "an array",
            JTokenType.Integer => "an integer",
            JTokenType.Float => "a number",
            JTokenType.Boolean => "a boolean",
            JTokenType.Null => "null",
            _ => value.Type.ToString().ToLowerInvariant()
        };
}