namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;
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
                    .HasData(new DbChatSettings
                    {
                        IdSettings = "general",
                        Model = "gpt-4-1106-preview",
                        Streaming = true,
                        FrequencyPenalty = 1,
                        PresencePenalty = 1,
                        Temperature = 1,
                        TopP = 1,
                        Premise = "You are a helpful but cynical and humorous assistant (but not over the top). " +
                                  "You give short answers, straight, to the point answers. Use md syntax and be " +
                                  "sure to specify language for code blocks."
                    });

        modelBuilder.Entity<DbCategory>()
                    .HasData(new DbCategory
                    {
                        IdCategory = "default",
                        IdSettings = "general",
                        Name = "General",
                        Description = "General Purpose AI Assistant"
                    });       
    }
}

public class DbConversation
{
    [Key] public string IdConversation { get; set; }
    public string IdCategory { get; set; }
    public string IdSettingsOverride { get; set; }
    public string Name { get; set; }
    public bool IsTrash { get; set; }
    public DateTime CreatedTs { get; set; }

    public DbCategory Category { get; set; }
    public List<DbMessage> Messages { get; set; } = new();
    public DbChatSettings SettingsOverride { get; set; }
}

public class DbMessage
{
    [Key] public string IdMessage { get; set; }
    public string IdMessageParent { get; set; }
    public string IdConversation { get; set; }
    public int Version { get; set; }
    public bool IsCurrentVersion { get; set; }
    public DateTime CreatedTs { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public bool IsTrash { get; set; }

    public DbConversation Conversation { get; set; }
}

public class DbCategory
{
    [Key] public string IdCategory { get; set; }
    public string IdSettings { get; set; }
    public string Name { get; set; }
    public string SystemMessage { get; set; } // TODO: Obsolete?
    public string Description { get; set; }

    public DbChatSettings Settings { get; set; }
}

public class DbChatSettings
{
    [Key] public string IdSettings { get; set; }
    public string Model { get; set; }
    public bool Streaming { get; set; } = true;
    public decimal Temperature { get; set; } = 1m;
    public decimal TopP { get; set; } = 1m;
    public decimal FrequencyPenalty { get; set; } = 1m;
    public decimal PresencePenalty { get; set; } = 1m;
    public string Premise { get; set; }
}