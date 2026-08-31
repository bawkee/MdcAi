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

/// <summary>Provider-neutral call-presentation intent (DSH proposal §6.6 / §7.2).</summary>
public enum ChatToolCallPresentationKind
{
    Generic,
    Terminal,
    Diff
}

/// <summary>Provider-neutral result-presentation intent (DSH proposal §6.6 / §7.2).</summary>
public enum ChatToolResultPresentationKind
{
    Generic,
    Terminal,
    Read,
    Search,
    Diff
}

/// <summary>
/// A versioned, locale-neutral, pure-data description of an intended tool call - rendered BEFORE
/// execution. Payload is display-safe structured data; no HTML, no secrets, no callbacks.
/// </summary>
public sealed record ChatToolCallPresentation(
    int Version,
    ChatToolCallPresentationKind Kind,
    string Title,
    string Summary,
    JObject Payload)
{
    public static ChatToolCallPresentation Generic(string title, string summary, JObject payload = null) =>
        new(1, ChatToolCallPresentationKind.Generic, title, summary, payload ?? new JObject());
}

/// <summary>
/// A versioned, locale-neutral, pure-data description of a completed tool result - rendered from
/// persisted intent on replay, never by re-running the tool (DSH proposal §7.2).
/// </summary>
public sealed record ChatToolResultPresentation(
    int Version,
    ChatToolResultPresentationKind Kind,
    string Title,
    string Summary,
    JObject Payload)
{
    public static ChatToolResultPresentation Generic(string title, string summary, JObject payload = null) =>
        new(1, ChatToolResultPresentationKind.Generic, title, summary, payload ?? new JObject());
}