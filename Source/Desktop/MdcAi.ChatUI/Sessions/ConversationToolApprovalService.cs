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

using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using ChatCore.Security;
using ChatCore.Tools;
using MdcAi.ChatUI.ViewModels;

/// <summary>
/// Main-conversation inline approval (DSH proposal §7.6): the runner awaits this service; the
/// pending request is exposed reactively so the renderer shows the proposed diff/exact script
/// with Approve/Deny. The WebView bridge resolves it by matching conversation/turn/tool-call id
/// AND the immutable argument hash - a stale renderer click can never approve a changed call.
/// Grants are held in memory, scoped to conversation + turn, never inferred from a persisted card.
/// </summary>
public sealed class ConversationToolApprovalService : IChatToolApprovalService
{
    private readonly object _gate = new();
    private ChatApprovalTask _pending;

    /// <summary>The pending approval request observable for the renderer (null when none).</summary>
    public IObservable<PendingApproval> Pending { get; }

    private readonly Subject<PendingApproval> _pendingSubject = new();

    public ConversationToolApprovalService()
    {
        Pending = _pendingSubject.AsObservable();
    }

    public ValueTask<ChatApprovalResponse> RequestApprovalAsync(ChatApprovalRequest request, CancellationToken ct)
    {
        var task = new ChatApprovalTask(request, ct);

        lock (_gate)
        {
            if (_pending != null)
            {
                // Only one pending approval per conversation turn; a second request completes the
                // first as cancelled (it can never execute without its own consent).
                _pending.Complete(ChatApprovalDecision.Denied);
            }

            _pending = task;
        }

        _pendingSubject.OnNext(new PendingApproval(request, Cancelled: false));

        ct.Register(() =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, task))
                    _pending = null;
            }

            task.Complete(ChatApprovalDecision.Denied);
            _pendingSubject.OnNext(new PendingApproval(null, Cancelled: true));
        });

        return new ValueTask<ChatApprovalResponse>(task.Task);
    }

    /// <summary>
    /// Resolves the pending approval. Returns false when the identifiers/hash don't match the
    /// CURRENT pending request (stale click). The task completes exactly once.
    /// </summary>
    public bool Resolve(string conversationId, string turnId, string toolCallId, string argumentsHash, ChatApprovalDecision decision)
    {
        ChatApprovalTask task;

        lock (_gate)
        {
            task = _pending;

            if (task == null)
                return false;
            if (task.Request.ConversationId != conversationId
                || task.Request.TurnId != turnId
                || task.Request.ToolCallId != toolCallId
                || task.Request.ArgumentsHash != argumentsHash)
                return false;

            _pending = null;
        }

        task.Complete(decision);
        _pendingSubject.OnNext(new PendingApproval(null, Cancelled: false));
        return true;
    }

    public ValueTask<bool> HasReadGrantAsync(string conversationId, string turnId, CancellationToken ct) =>
    new(false);

    private sealed class ChatApprovalTask
    {
        private readonly TaskCompletionSource<ChatApprovalResponse> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ChatApprovalRequest Request { get; }

        public ChatApprovalTask(ChatApprovalRequest request, CancellationToken ct)
        {
            Request = request;
        }

        public Task<ChatApprovalResponse> Task => _tcs.Task;

        public void Complete(ChatApprovalDecision decision)
        {
            _tcs.TrySetResult(new ChatApprovalResponse(
                Request.ConversationId, Request.TurnId, Request.ToolCallId,
                Request.ArgumentsHash, decision));
        }
    }
}

/// <summary>Renderer-facing pending approval: the call presentation (proposed diff/exact script) + ids/hash.</summary>
public sealed record PendingApproval(
    ChatApprovalRequest Request,
    bool Cancelled)
{
    /// <summary>A host-authored, immutable approval card for the renderer.</summary>
    public WebViewActivityDto ToActivity()
    {
        if (Request == null)
            return null;

        return new WebViewActivityDto
        {
            ActivityKind = "tool",
            PresentationKind = "diff",
            Status = "awaiting_approval",
            Title = Request.Presentation.Title ?? Request.ToolName,
            Summary = Request.Presentation.Summary,
            ToolCallId = Request.ToolCallId,
            ArgumentHash = Request.ArgumentsHash,
            Details = new WebViewActivityDetailsDto
            {
                Version = 1,
                Kind = Request.Presentation.Kind == ChatToolCallPresentationKind.Terminal ? "terminal" : "diff",
                Terminal = Request.Presentation.Kind == ChatToolCallPresentationKind.Terminal
                               ? new WebViewTerminalDetailsDto
                               {
                                   Script = (string)Request.Presentation.Payload?["script"],
                                   WorkingDirectory = (string)Request.Presentation.Payload?["working_directory"]
                               }
                               : null,
                Diff = Request.Presentation.Kind == ChatToolCallPresentationKind.Diff
                           ? new WebViewDiffDetailsDto
                           {
                               State = "proposed",
                               Diffs = new[]
                               {
                                   new WebViewDiffFileDto
                                   {
                                       Path = (string)Request.Presentation.Payload?["path"],
                                       OldText = "",
                                       NewText = (string)Request.Presentation.Payload?["proposed"]?["new_text"]
                                   }
                               }
                           }
                           : null
            }
        };
    }
}