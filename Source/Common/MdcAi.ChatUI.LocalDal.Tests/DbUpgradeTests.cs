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

namespace MdcAi.ChatUI.LocalDal.Tests;

using Microsoft.Data.Sqlite;

/// <summary>
/// Database upgrade/schema tests for the Phase 1 agentic checkpoints (DSH proposal §6.5):
/// a copied pre-migration database upgrades with its rows intact, the fresh embedded database
/// starts at the new schema, and the new relational checkpoints round-trip.
/// </summary>
public class DbUpgradeTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "mdcai-dbtests-" + Guid.NewGuid().ToString("N"));

    public DbUpgradeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private string TempDb(string name) => Path.Combine(_tempDir, name);

    /// <summary>Replays the pre-agentic migration script into a fresh SQLite file.</summary>
    private static void ApplyPreAgenticScript(string dbPath)
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "pre-agentic.sql"));
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = script;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Fresh_embedded_database_starts_at_new_schema()
    {
        // The shipped Chats.db IS the fresh-install database; a copied file must load as-is.
        var copy = TempDb("embedded-copy.db");
        await using (var source = new UserProfileDbContext())
        {
            await using var embedded = typeof(UserProfileDbContext).Assembly
                .GetManifestResourceStream("MdcAi.ChatUI.LocalDal.Chats.db");
            await using var file = File.Create(copy);
            await embedded.CopyToAsync(file);
        }

        await using var db = new UserProfileDbContext(copy);
        await db.Database.MigrateAsync(); // no pending migrations - at head already

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Turns.CountAsync());
        Assert.Equal(1, await db.ChatSettings.CountAsync());

        // Fresh install seeds survive.
        var settings = await db.ChatSettings.FirstAsync();
        Assert.Equal("gpt-4o", settings.Model);
        Assert.Null(settings.ProviderKey);
    }

    [Fact]
    public async Task Legacy_database_upgrades_and_preserves_rows()
    {
        var legacy = TempDb("legacy.db");
        ApplyPreAgenticScript(legacy);

        // A legacy conversation with fork-ish rows and per-message provenance but no agentic columns.
        using (var conn = new SqliteConnection($"Data Source={legacy}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO "Conversations" ("IdConversation", "IdCategory", "Name", "IsTrash", "CreatedTs")
                              VALUES ('conv-1', 'default', 'Legacy chat', 0, '2026-01-01 10:00:00');
                              INSERT INTO "Messages" ("IdMessage", "IdMessageParent", "IdConversation", "Version", "IsCurrentVersion", "CreatedTs", "Role", "Content", "Model", "Effort", "Reasoning", "IsTrash")
                              VALUES ('m-1', NULL, 'conv-1', 1, 1, '2026-01-01 10:00:05', 'user', 'hello', NULL, NULL, NULL, 0);
                              INSERT INTO "Messages" ("IdMessage", "IdMessageParent", "IdConversation", "Version", "IsCurrentVersion", "CreatedTs", "Role", "Content", "Model", "Effort", "Reasoning", "IsTrash")
                              VALUES ('m-2', 'm-1', 'conv-1', 1, 1, '2026-01-01 10:00:10', 'assistant', 'hi there', 'gpt-4o', 'medium', 'think about it', 0);
                              """;
            cmd.ExecuteNonQuery();
        }

        await using var db = new UserProfileDbContext(legacy);
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
        await db.Database.MigrateAsync();

        var conversation = await db.Conversations
                                   .Include(c => c.Messages)
                                   .FirstAsync(c => c.IdConversation == "conv-1");

        // Legacy rows untouched.
        Assert.Equal(2, conversation.Messages.Count);
        Assert.False(conversation.ToolsEnabled);
        Assert.Null(conversation.WorkspacePath);
        Assert.Equal("hi there", conversation.Messages.Single(m => m.IdMessage == "m-2").Content);
        Assert.Equal("gpt-4o", conversation.Messages.Single(m => m.IdMessage == "m-2").Model);
        Assert.Equal("medium", conversation.Messages.Single(m => m.IdMessage == "m-2").Effort);

        // New columns exist and are null on legacy rows.
        var m2 = conversation.Messages.Single(m => m.IdMessage == "m-2");
        Assert.Null(m2.ToolCallsJson);
        Assert.Null(m2.Origin);
        Assert.Null(m2.ToolCallId);

        // New tables exist and are queryable.
        Assert.Equal(0, await db.Turns.CountAsync());
        Assert.Equal(0, await db.Steps.CountAsync());
        Assert.Equal(0, await db.ToolCalls.CountAsync());
        Assert.Equal(0, await db.ModelRequestAttempts.CountAsync());
    }

    [Fact]
    public async Task Agentic_checkpoints_round_trip_through_ef()
    {
        var dbPath = TempDb("agentic.db");

        await using (var db = new UserProfileDbContext(dbPath))
        {
            await db.Database.MigrateAsync();

            db.Conversations.Add(new DbConversation
            {
                IdConversation = "c-1",
                IdCategory = "default",
                Name = "Agentic",
                CreatedTs = DateTime.UtcNow,
                ToolsEnabled = true,
                WorkspacePath = @"C:\workspace"
            });

            db.Turns.Add(new DbChatTurn
            {
                IdTurn = "turn-1",
                IdConversation = "c-1",
                IdTriggerMessage = "m-user-1",
                Origin = "human",
                Status = "completed",
                Outcome = "Completed",
                ProviderKey = "openrouter",
                Model = "deepseek/deepseek-chat",
                Effort = "medium",
                StartedTs = DateTime.UtcNow,
                EndedTs = DateTime.UtcNow,
                StepCount = 2,
                Steps =
                {
                    new DbChatStep
                    {
                        IdStep = "step-1",
                        StepNumber = 1,
                        FinishReason = "tool_calls",
                        ProviderKey = "openrouter",
                        Model = "deepseek/deepseek-chat",
                        PromptTokens = 100,
                        CompletionTokens = 12,
                        TotalTokens = 112,
                        Attempts =
                        {
                            new DbModelRequestAttempt
                            {
                                IdAttempt = "attempt-1-1",
                                AttemptNumber = 1,
                                Status = "completed",
                                RetryDisposition = "none",
                                StartedTs = DateTime.UtcNow,
                                EndedTs = DateTime.UtcNow
                            }
                        }
                    },
                    new DbChatStep
                    {
                        IdStep = "step-2",
                        StepNumber = 2,
                        FinishReason = "stop"
                    }
                }
            });

            db.ToolCalls.Add(new DbToolCall
            {
                IdToolCall = "tc-1",
                IdAssistantMessage = "m-assistant-1",
                IdTurn = "turn-1",
                IdStep = "step-1",
                ToolCallId = "call_1",
                CallIndex = 0,
                ToolName = "read_file",
                ArgumentsJson = """{"path":"a.txt"}""",
                ArgumentsHash = "abc123",
                Risk = "readonly",
                Status = "completed"
            });

            await db.SaveChangesAsync();

            // Same step number / call index rejected by the unique indexes.
            db.Steps.Add(new DbChatStep { IdStep = "step-dup", IdTurn = "turn-1", StepNumber = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Reload in a fresh context and verify the round trip.
        await using var reload = new UserProfileDbContext(dbPath);
        var turn = await reload.Turns
                               .Include(t => t.Steps).ThenInclude(s => s.Attempts)
                               .FirstAsync(t => t.IdTurn == "turn-1");
        Assert.Equal(2, turn.Steps.Count);
        Assert.Equal("call_1", (await reload.ToolCalls.FirstAsync()).ToolCallId);
        Assert.Equal(100, turn.Steps[0].PromptTokens);
        Assert.Equal("completed", turn.Steps[0].Attempts.Single().Status);
        Assert.True((await reload.Conversations.FirstAsync(c => c.IdConversation == "c-1")).ToolsEnabled);
    }
}