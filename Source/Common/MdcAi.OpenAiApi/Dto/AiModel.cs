namespace MdcAi.OpenAiApi;

using Newtonsoft.Json.Converters;

internal class AiModels
{
    [JsonProperty("data")] public AiModel[] Models { get; set; }
}

public class AiModel
{
    [JsonProperty("id")] public string ModelID { get; set; }
    [JsonProperty("owned_by")] public string OwnedBy { get; set; }
    [JsonProperty("object")] public string Object { get; set; }

    [JsonConverter(typeof(UnixDateTimeConverter))]
    [JsonProperty("created")]
    public DateTime? Created { get; set; }

    [JsonProperty("permission")] public Permissions[] Permission { get; set; }

    public static implicit operator string(AiModel model) => model?.ModelID;
    public static implicit operator AiModel(string name) => new(name);

    public AiModel(string name) { ModelID = name; }

    public static AiModel AdaTextEmbedding => new AiModel("text-embedding-ada-002") { OwnedBy = "openai" };
    public static AiModel GPT35Turbo => new AiModel("gpt-3.5-turbo-1106") { OwnedBy = "openai" };
    public static AiModel GPT35Turbo0301 => new AiModel("gpt-3.5-turbo-0301") { OwnedBy = "openai" };
    public static AiModel GPT4Turbo => new AiModel("gpt-4-1106-preview") { OwnedBy = "openai" };
    public static AiModel GPT4 => new AiModel("gpt-4") { OwnedBy = "openai" };

    public override string ToString() => ModelID;
}