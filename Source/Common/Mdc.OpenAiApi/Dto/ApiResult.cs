namespace Mdc.OpenAiApi;

using Newtonsoft.Json.Converters;

public class ApiResult
{
    [JsonConverter(typeof(UnixDateTimeConverter))]
    [JsonProperty("created")]
    public DateTime? Created { get; set; }

    [JsonProperty("model")] public string Model { get; set; }
    [JsonProperty("object")] public string Object { get; set; }
    [JsonIgnore] public string Organization { get; internal set; }
    [JsonIgnore] public TimeSpan ProcessingTime { get; internal set; }
    [JsonIgnore] public string RequestId { get; internal set; }
    [JsonIgnore] public string OpenaiVersion { get; internal set; }
}