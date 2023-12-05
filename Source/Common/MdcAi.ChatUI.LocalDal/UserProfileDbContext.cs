namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

public class UserProfileDbContext : DbContext
{
    public DbSet<DbConversation> Conversations { get; set; }
    public DbSet<DbMessage> Messages { get; set; }

    public string DbPath { get; }

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
        optionsBuilder.LogTo(message =>
        {
            if (message.Contains("CommandExecuted"))
                Debug.WriteLine(message);
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbConversation>()
            .HasMany(e => e.Messages)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.IdConversation);                      
    }
}

public class DbConversation
{
    [Key] public string IdConversation { get; set; }
    public string Name { get; set; }    
    public string Category { get; set; }
    public bool IsTrash { get; set; }
    public DateTime CreatedTs { get; set; }

    public List<DbMessage> Messages { get; set; } = new();
}

public class DbMessage
{
    // TODO: Remove redundant values
    // TODO: Add IsTrashed
    [Key] public string IdMessage { get; set; }
    public string IdMessageParent { get; set; }
    public string IdConversation { get; set; }
    public int Version { get; set; }
    public bool IsCurrentVersion { get; set; }
    public int Index { get; set; } // Redundant?
    public DateTime CreatedTs { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public string HTMLContent { get; set; } // Redundant?    

    public DbConversation Conversation { get; set; }
}