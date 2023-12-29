namespace MdcAi.ChatUI.LocalDal;

using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

public class UserProfileDbContext : DbContext
{
    public DbSet<DbConversation> Conversations { get; set; }
    public DbSet<DbMessage> Messages { get; set; }
    public DbSet<DbCategory> Categories { get; set; }
    public DbSet<DbChatSettings> ChatSettings { get; set; }

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
            Model = "gpt-4-1106-preview",
            Streaming = true,
            FrequencyPenalty = 1,
            PresencePenalty = 1,
            Temperature = 1,
            TopP = 1,
            Premise = "You are a helpful but cynical and humorous assistant (but not over the top). " +
                      "You give short and straight to the point answers."
        };
}