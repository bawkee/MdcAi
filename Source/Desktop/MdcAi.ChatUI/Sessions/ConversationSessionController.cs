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

using System.Threading;

/// <summary>
/// The durable identity of one active turn. Captured AT TURN START and stamped onto every
/// node/checkpoint so the provider/model/effort/tool schema never varies mid-turn
/// (DSH proposal §9.2).
/// </summary>
public sealed record SessionTurnContext(
    string TurnId,
    string TriggerMessageId,
    string ProviderKey,
    string Model,
    string Effort,
    string WorkspacePath,
    string Origin);

/// <summary>
/// One active turn per conversation: owns the cancellation source and serializes turn
/// execution with a one-turn mutex. Survives navigation (constructor scope, like the existing
/// completion subscriptions) so switching conversations never cancels the run.
/// </summary>
public sealed class ConversationSessionController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _cts;

    public bool IsTurnActive { get; private set; }

    /// <summary>Id of the active turn (for approval matching); null when idle.</summary>
    public string ActiveTurnId { get; private set; }

    /// <summary>Fired when <see cref="IsTurnActive"/> changes (start/stop of a turn).</summary>
    public event Action ActiveChanged;

    /// <summary>
    /// Runs one turn if none is active. The runner factory receives the turn's cancellation
    /// token; the controller guarantees at most one in-flight turn per conversation.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task> turnRunner, string turnId = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsTurnActive)
                return;

            _cts = new CancellationTokenSource();
            IsTurnActive = true;
            ActiveTurnId = turnId;
            ActiveChanged?.Invoke();

            try
            {
                await turnRunner(_cts.Token);
            }
            finally
            {
                IsTurnActive = false;
                ActiveTurnId = null;
                _cts.Dispose();
                _cts = null;
                ActiveChanged?.Invoke();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Requests cancellation of the active turn (streams, tools, backoff).</summary>
    public void Stop() => _cts?.Cancel();
}