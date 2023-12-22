namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

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