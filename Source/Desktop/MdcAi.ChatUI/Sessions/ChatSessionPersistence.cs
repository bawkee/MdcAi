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
using LocalDal;
using Microsoft.EntityFrameworkCore;
using MdcAi.Extensions.WinUI;

/// <summary>
/// Durable run-checkpoint persistence behind the session sink (DSH proposal §6.5). The sink
/// writes checkpoint state through this seam so tests can substitute an in-memory fake and the
/// WinUI app uses the real SQLite repository (a transient EF context per write - sequential,
/// never Task.WhenAll on one context).
/// </summary>
public interface IChatSessionPersistence
{
    Task SaveTurnCheckpointAsync(DbChatTurn turn, CancellationToken ct);
}

/// <summary>
/// SQLite implementation via the app's transient UserProfileDbContext. When the container has
/// not been installed (plain unit tests) checkpointing is a deliberate no-op - the transcript
/// behavior is what those tests exercise, not durable storage.
/// </summary>
public sealed class SqliteChatSessionPersistence : IChatSessionPersistence
{
    public async Task SaveTurnCheckpointAsync(DbChatTurn turn, CancellationToken ct)
    {
        if (AppServices.Container == null)
            return;

        await using var db = AppServices.GetUserProfileDb();
        var existing = await db.Turns.FirstOrDefaultAsync(t => t.IdTurn == turn.IdTurn, ct);

        if (existing == null)
        {
            db.Turns.Add(turn);
        }
        else
        {
            // Preserve the navigation-free scalar surface only; never touch tracked children.
            db.Entry(existing).CurrentValues.SetValues(turn);
        }

        await db.SaveChangesAsync(ct);
    }
}