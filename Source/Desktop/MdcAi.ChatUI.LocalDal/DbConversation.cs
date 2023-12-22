namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

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