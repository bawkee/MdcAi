namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

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