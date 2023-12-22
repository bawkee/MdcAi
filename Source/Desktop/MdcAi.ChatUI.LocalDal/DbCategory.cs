namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

public class DbCategory
{
    [Key] public string IdCategory { get; set; }
    public string IdSettings { get; set; }
    public string Name { get; set; }
    public bool IsTrash { get; set; }
    public string IconGlyph { get; set; }
    public string Description { get; set; }
    
    public DbChatSettings Settings { get; set; }
}