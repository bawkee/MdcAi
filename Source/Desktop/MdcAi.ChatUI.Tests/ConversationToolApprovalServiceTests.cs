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

namespace MdcAi.ChatUI.Tests;

using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatUI.Sessions;
using Newtonsoft.Json.Linq;

/// <summary>
/// P2-05 inline approval: the pending request is exposed reactively, resolved exactly once via
/// matching conversation/turn/tool-call id AND the immutable argument hash, and a stale click
/// (wrong hash/id) is rejected without resolving anything.
/// </summary>
public class ConversationToolApprovalServiceTests
{
    private static ChatApprovalRequest Request(string toolCallId = "call_1", string hash = "HASH") =>
        new("c1", "turn-1", toolCallId, "write_file", ChatToolRisk.Write,
            ChatToolCallPresentation.Diff("Write", "Write · a.txt", new JObject { ["path"] = "a.txt" }),
            hash);

    [Fact]
    public async Task Pending_request_is_exposed_and_resolves_exactly_once()
    {
        var service = new ConversationToolApprovalService();
        PendingApproval last = null;
        var sub = service.Pending.Subscribe(p => last = p);

        var responseTask = service.RequestApprovalAsync(Request(), CancellationToken.None).AsTask();

        // A pending card surfaced to the renderer.
        Assert.NotNull(last);
        Assert.Equal("awaiting_approval", last.ToActivity().Status);
        Assert.Equal("call_1", last.ToActivity().ToolCallId);
        Assert.Equal("HASH", last.ToActivity().ArgumentHash);

        // Resolve with the matching hash.
        var ok = service.Resolve("c1", "turn-1", "call_1", "HASH", ChatApprovalDecision.Approved);
        Assert.True(ok);

        var response = await responseTask;
        Assert.Equal(ChatApprovalDecision.Approved, response.Decision);
        Assert.Equal("call_1", response.ToolCallId);

        // After resolution there is no pending card.
        Assert.Null(last.Request);
        sub.Dispose();
    }

    [Fact]
    public async Task Stale_hash_click_is_rejected_and_does_not_resolve()
    {
        var service = new ConversationToolApprovalService();
        var responseTask = service.RequestApprovalAsync(Request(), CancellationToken.None).AsTask();

        // Wrong argument hash -> rejected (false), the request stays pending.
        Assert.False(service.Resolve("c1", "turn-1", "call_1", "WRONG", ChatApprovalDecision.Approved));
        Assert.False(service.Resolve("c1", "turn-1", "other-call", "HASH", ChatApprovalDecision.Approved));
        Assert.False(service.Resolve("c1", "other-turn", "call_1", "HASH", ChatApprovalDecision.Approved));
        Assert.False(service.Resolve("other-convo", "turn-1", "call_1", "HASH", ChatApprovalDecision.Approved));

        // Still pending; resolve correctly now.
        Assert.True(service.Resolve("c1", "turn-1", "call_1", "HASH", ChatApprovalDecision.Denied));
        Assert.Equal(ChatApprovalDecision.Denied, (await responseTask).Decision);
    }

    [Fact]
    public async Task Second_pending_request_cancels_the_first()
    {
        var service = new ConversationToolApprovalService();

        var first = service.RequestApprovalAsync(Request("call_1", "H1"), CancellationToken.None).AsTask();
        // A second pending request (e.g. while approval UI hasn't flushed) completes the first denied.
        var second = service.RequestApprovalAsync(Request("call_2", "H2"), CancellationToken.None);

        Assert.Equal(ChatApprovalDecision.Denied, (await first).Decision);
        Assert.False(second.IsCompleted); // the second remains pending until reased/overwritten
    }

    [Fact]
    public async Task Cancellation_completes_the_pending_request_denied()
    {
        using var cts = new CancellationTokenSource();
        var service = new ConversationToolApprovalService();

        var responseTask = service.RequestApprovalAsync(Request(), cts.Token).AsTask();
        cts.Cancel();

        var response = await responseTask;
        Assert.Equal(ChatApprovalDecision.Denied, response.Decision);
    }
}