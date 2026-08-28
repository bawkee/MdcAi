#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.OpenAiApi.Tests;

public class ErrorMappingTests
{
    private static readonly Uri BaseUri = new("https://openrouter.ai/api/v1/");

    private static async Task<Exception> RequestThrows(FakeHttpMessageHandler handler)
    {
        using var client = handler.Client(BaseUri);
        try
        {
            await client.RequestAsync<ChatResult>(new Uri("chat/completions", UriKind.Relative), HttpMethod.Post);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task Invalid_api_key_code_maps_to_specific_exception()
    {
        var ex = await RequestThrows(FakeHttpMessageHandler.Status(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error","code":"invalid_api_key"}}"""));

        Assert.IsType<OpenAiInvalidApiKeyException>(ex);
        Assert.Contains("Incorrect API key provided", ex.Message);
    }

    [Fact]
    public async Task Payment_required_maps_to_quota_exception()
    {
        var ex = await RequestThrows(FakeHttpMessageHandler.Status(
            HttpStatusCode.PaymentRequired,
            """{"error":{"message":"Insufficient credits. Check https://openrouter.ai/account to add credits.","code":402}}"""));

        Assert.IsType<OpenAiApiQuotaException>(ex);
    }

    [Fact]
    public async Task Too_many_requests_maps_to_quota_exception()
    {
        var ex = await RequestThrows(FakeHttpMessageHandler.Status(
            HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit reached for model","code":429}}"""));

        Assert.IsType<OpenAiApiQuotaException>(ex);
    }

    [Fact]
    public async Task Server_error_maps_to_generic_api_exception()
    {
        var ex = await RequestThrows(FakeHttpMessageHandler.Status(
            HttpStatusCode.InternalServerError,
            """{"error":{"message":"Provider had an internal error","code":500}}"""));

        Assert.IsType<OpenAiApiException>(ex);
        Assert.IsNotType<OpenAiApiAuthException>(ex);
        Assert.IsNotType<OpenAiApiQuotaException>(ex);
    }

    [Fact]
    public async Task Unknown_error_shapes_become_generic_exception_with_body()
    {
        var ex = await RequestThrows(FakeHttpMessageHandler.Status(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"Model not found","code":404}}"""));

        Assert.IsType<OpenAiApiException>(ex);
    }

    [Fact]
    public async Task Non_json_error_body_still_throws()
    {
        var ex = await RequestThrows(FakeHttpMessageHandler.Status(
            HttpStatusCode.BadGateway,
            "502 Bad Gateway from upstream provider"));

        Assert.IsType<OpenAiApiException>(ex);
    }
}