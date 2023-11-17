namespace Mdc.OpenAiApi;

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

    /// <summary>
    /// Currently (2023-01-27) seems like this is duplicate of <see cref="ModelID"/> but including for completeness.
    /// </summary>
    [JsonProperty("root")] public string Root { get; set; }

    /// <summary>
    /// Currently (2023-01-27) seems unused, probably intended for nesting of models in a later release
    /// </summary>
    [JsonProperty("parent")] public string Parent { get; set; }

    public static implicit operator string(AiModel model) => model?.ModelID;

    public static implicit operator AiModel(string name) => new(name);

    public AiModel(string name) { ModelID = name; }

    public static AiModel AdaTextEmbedding => new AiModel("text-embedding-ada-002") { OwnedBy = "openai" };

    public static AiModel GPT35Turbo => new AiModel("gpt-3.5-turbo-1106") { OwnedBy = "openai" };

    public static AiModel GPT35Turbo0301 => new AiModel("gpt-3.5-turbo-0301") { OwnedBy = "openai" };

    public static AiModel GPT4Turbo => new AiModel("gpt-4-1106-preview") { OwnedBy = "openai" };

    public static AiModel GPT4 => new AiModel("gpt-4") { OwnedBy = "openai" };

    /// <summary>
    /// Same capabilities as the base gpt-4 mode but with 4x the context length. Will be updated with the latest model iteration.  Currently in limited beta so your OpenAI account needs to be whitelisted to use this.
    /// </summary>
    public static AiModel GPT432k => new AiModel("gpt-4-32k") { OwnedBy = "openai" };
}