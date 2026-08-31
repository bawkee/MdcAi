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

/// <summary>
/// An immutable, case-sensitive name→tool dictionary built at startup. Fails fast on duplicate
/// or invalid tool names. Talk to it through <see cref="Filtered"/> for helper-scoped allowlists.
/// </summary>
public sealed class ChatToolRegistry
{
    private readonly IReadOnlyDictionary<string, IChatTool> _tools;

    public IReadOnlyCollection<IChatTool> All => _tools.Values.ToArray();

    private ChatToolRegistry(IReadOnlyDictionary<string, IChatTool> tools) => _tools = tools;

    public static ChatToolRegistry Build(IEnumerable<IChatTool> tools)
    {
        var list = tools?.ToArray() ?? Array.Empty<IChatTool>();

        var duplicate = list.GroupBy(t => t.Name, StringComparer.Ordinal)
                            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate tool name '{duplicate.Key}'.");

        foreach (var tool in list)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new InvalidOperationException("A tool must have a non-empty name.");
            if (tool.ParametersSchema == null)
                throw new InvalidOperationException($"Tool '{tool.Name}' must declare a parameters schema.");
        }

        return new ChatToolRegistry(list.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal));
    }

    public bool Contains(string name) => name != null && _tools.ContainsKey(name);

    public bool TryGet(string name, out IChatTool tool) => _tools.TryGetValue(name, out tool);

    /// <summary>Builds a read-only projection with only the allowed tool names (for helper sessions).</summary>
    public ChatToolRegistry Filtered(IEnumerable<string> allowedNames)
    {
        var set = new HashSet<string>(allowedNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var subset = _tools.Values.Where(t => set.Contains(t.Name));
        return Build(subset);
    }

    /// <summary>The OpenAI-compatible tool array advertised on the wire for the enabled names.</summary>
    public OpenAiApi.ChatTool[] ToWireTools(IEnumerable<string> enabledNames)
    {
        var set = new HashSet<string>(enabledNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        return _tools.Values
                     .Where(t => set.Contains(t.Name))
                     .Select(t => new OpenAiApi.ChatTool
                     {
                         Type = "function",
                         Function = new OpenAiApi.FunctionTool
                         {
                             Name = t.Name,
                             Description = t.Description,
                             Parameters = t.ParametersSchema
                         }
                     })
                     .ToArray();
    }
}