namespace MdcAi.OpenAiApi.IntegrationTests;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Reads local test secrets (dotnet user-secrets) + environment variables.
///
/// Set the keys with:
///   dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-..." --project "&lt;repo&gt;\Source\Common\MdcAi.OpenAiApi.IntegrationTests\MdcAi.OpenAiApi.IntegrationTests.csproj"
///   dotnet user-secrets set "OpenAI:ApiKey"      "sk-..."    --project "&lt;same project&gt;"
///
/// When a provider's key is absent the affected tests early-return and count as passed, so the
/// suite stays green on machines that only configured one provider.
/// </summary>
public static class TestSecrets
{
    private static readonly IConfigurationRoot Config = new ConfigurationBuilder()
        .AddUserSecrets(typeof(TestSecrets).Assembly)
        .AddEnvironmentVariables()
        .Build();

    public static string OpenRouterApiKey => Config["OpenRouter:ApiKey"];

    public static string OpenAiApiKey => Config["OpenAI:ApiKey"];

    public static bool HasOpenRouterKey => !string.IsNullOrEmpty(OpenRouterApiKey);

    public static bool HasOpenAiKey => !string.IsNullOrEmpty(OpenAiApiKey);
}