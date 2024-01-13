namespace MdcAi.OpenAiApi;

// https://platform.openai.com/docs/guides/error-codes/api-errors

public class OpenAiApiException : Exception
{
    public OpenAiApiException(string message)
        : base(message) { }
}

public class OpenAiApiAuthException : OpenAiApiException
{
    public OpenAiApiAuthException(string message)
        : base(message) { }
}

public class OpenAiInvalidApiKeyException : OpenAiApiAuthException
{
    public OpenAiInvalidApiKeyException(string message)
        : base(message) { }
}

public class OpenAiApiQuotaException : OpenAiApiException
{
    public OpenAiApiQuotaException(string message)
        : base(message) { }
}