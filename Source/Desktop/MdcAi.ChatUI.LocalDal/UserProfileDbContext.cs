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

namespace MdcAi.ChatUI.LocalDal;

using System.Reflection;
using Microsoft.EntityFrameworkCore;

public class UserProfileDbContext : DbContext
{
    public DbSet<DbConversation> Conversations { get; set; }
    public DbSet<DbMessage> Messages { get; set; }
    public DbSet<DbCategory> Categories { get; set; }
    public DbSet<DbChatSettings> ChatSettings { get; set; }
    public DbSet<DbChatTurn> Turns { get; set; }
    public DbSet<DbChatStep> Steps { get; set; }
    public DbSet<DbModelRequestAttempt> ModelRequestAttempts { get; set; }
    public DbSet<DbToolCall> ToolCalls { get; set; }
    public DbSet<DbBackgroundJob> BackgroundJobs { get; set; }

    public string DbPath { get; }
    public Action<string> Log { get; set; }

    public UserProfileDbContext() { }

    public UserProfileDbContext(string dbPath)
        : this()
    {
        DbPath = dbPath;

        if (File.Exists(DbPath))
            return;

        using var stream = Assembly.GetExecutingAssembly()
                                   .GetManifestResourceStream("MdcAi.ChatUI.LocalDal.Chats.db");
        using var fileStream = new FileStream(DbPath, FileMode.Create, FileAccess.Write, FileShare.None);

        stream.CopyTo(fileStream);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
            return;

        var connString = DbPath == null ? null : $"Data Source={DbPath}";
        optionsBuilder.UseSqlite(connString);
        if (Log != null)
            optionsBuilder.LogTo(Log);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbConversation>()
                    .HasMany(e => e.Messages)
                    .WithOne(e => e.Conversation)
                    .HasForeignKey(e => e.IdConversation);

        modelBuilder.Entity<DbConversation>()
                    .HasOne(c => c.SettingsOverride)
                    .WithMany()
                    .HasForeignKey(c => c.IdSettingsOverride)
                    .IsRequired(false);

        modelBuilder.Entity<DbConversation>()
                    .HasOne(c => c.Category)
                    .WithMany()
                    .HasForeignKey(c => c.IdCategory)
                    .IsRequired(false);

        modelBuilder.Entity<DbCategory>()
                    .HasOne(c => c.Settings)
                    .WithMany()
                    .HasForeignKey(c => c.IdSettings)
                    .IsRequired();

        // --- Phase 1 agentic checkpoints ---
        // A conversation owns many runs; deleting a conversation branch is done explicitly in
        // one transaction so no cascade path can erase a fork unexpectedly (ClientSetNull).
        modelBuilder.Entity<DbChatTurn>()
                    .HasOne<DbConversation>()
                    .WithMany(c => c.Turns)
                    .HasForeignKey(t => t.IdConversation)
                    .OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<DbChatTurn>()
                    .HasMany(t => t.Steps)
                    .WithOne(s => s.Turn)
                    .HasForeignKey(s => s.IdTurn)
                    .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DbChatTurn>()
                    .HasMany(t => t.Messages)
                    .WithOne(m => m.Turn)
                    .HasForeignKey(m => m.IdTurn)
                    .OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<DbChatStep>()
                    .HasMany(s => s.Messages)
                    .WithOne(m => m.Step)
                    .HasForeignKey(m => m.IdStep)
                    .OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<DbChatStep>()
                    .HasMany(s => s.Attempts)
                    .WithOne(a => a.Step)
                    .HasForeignKey(a => a.IdStep)
                    .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DbChatStep>()
                    .HasIndex(s => new { s.IdTurn, s.StepNumber })
                    .IsUnique();

        modelBuilder.Entity<DbModelRequestAttempt>()
                    .HasIndex(a => new { a.IdStep, a.AttemptNumber })
                    .IsUnique();

        modelBuilder.Entity<DbToolCall>()
                    .HasIndex(t => new { t.IdAssistantMessage, t.ToolCallId })
                    .IsUnique();

        modelBuilder.Entity<DbToolCall>()
                    .HasIndex(t => new { t.IdAssistantMessage, t.CallIndex })
                    .IsUnique();

        modelBuilder.Entity<DbChatTurn>()
                    .HasIndex(t => t.IdConversation);

        modelBuilder.Entity<DbChatTurn>()
                    .HasIndex(t => t.IdTriggerMessage);

        modelBuilder.Entity<DbBackgroundJob>()
                    .HasIndex(j => j.OwnerConversationId);

        modelBuilder.Entity<DbBackgroundJob>()
                    .HasIndex(j => j.Status);

        modelBuilder.Entity<DbChatSettings>()
                    .HasData(CreateDefaultChatSettings("general"));

        modelBuilder.Entity<DbCategory>()
                    .HasData(new DbCategory
                    {
                        IdCategory = "default",
                        IdSettings = "general",
                        Name = "General",
                        Description = "General Purpose AI Assistant"
                    });
    }

    public DbChatSettings CreateDefaultChatSettings(string id) =>
        new()
        {
            IdSettings = id,
            Model = "gpt-4o",
            Streaming = true,
            FrequencyPenalty = 1,
            PresencePenalty = 1,
            Temperature = 1,
            TopP = 1,
            Premise = "You are a helpful but cynical and humorous assistant (but not over the top). " +
                      "You give short and straight to the point answers."
        };
}