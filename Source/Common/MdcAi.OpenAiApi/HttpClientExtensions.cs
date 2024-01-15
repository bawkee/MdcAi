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

namespace MdcAi.OpenAiApi;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SalaTools.Core;

/// <summary>
/// Http client wrapper specially made for OpenAi REST endpoints, their headers, return codes, errors, etc.
/// </summary>
public static class HttpClientExtensions
{
    private static void ParseHeaders(HttpResponseHeaders headers, ApiResult result)
    {
        try
        {
            result.Organization = headers.GetValues("Openai-Organization").FirstOrDefault();
            result.RequestId = headers.GetValues("X-Request-ID").FirstOrDefault();
            result.ProcessingTime = TimeSpan.FromMilliseconds(int.Parse(headers.GetValues("Openai-Processing-Ms").First()));
            result.OpenaiVersion = headers.GetValues("Openai-Version").FirstOrDefault();
            if (string.IsNullOrEmpty(result.Model))
                result.Model = headers.GetValues("Openai-Model").FirstOrDefault();
        }
        catch (Exception ex)
        {
            typeof(HttpClientExtensions).GetLogger()?.LogError(ex, "Parsing metadata of OpenAi Response");
        }
    }

    public static void AddOrUpdateDefaultHeader(this HttpClient client, string headerName, string headerValue, bool removeOnNull = true)
    {
        if (client.DefaultRequestHeaders.Contains(headerName))
            client.DefaultRequestHeaders.Remove(headerName);
        if (!removeOnNull || !string.IsNullOrEmpty(headerValue))
            client.DefaultRequestHeaders.Add(headerName, headerValue);
    }

    public static async Task<T> RequestAsync<T>(
        this HttpClient client,
        Uri uri,
        HttpMethod verb,
        object postData = null) where T : ApiResult
    {
        var response = await client.RequestAsync(uri, verb, postData);
        var resultAsString = await response.Content.ReadAsStringAsync();
        var res = JsonConvert.DeserializeObject<T>(resultAsString);

        ParseHeaders(response.Headers, res);

        return res;
    }

    public static async IAsyncEnumerable<T> RequestStreamingAsync<T>(
        this HttpClient client,
        Uri uri,
        HttpMethod verb,
        object postData = null) where T : ApiResult
    {
        var response = await client.RequestAsync(uri, verb, postData, true);

        var headers = new ApiResult();

        ParseHeaders(response.Headers, headers);

        await using var stream = await response.Content.ReadAsStreamAsync();

        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("data:"))
                line = line.Substring("data:".Length);

            line = line.TrimStart();

            if (line == "[DONE]")
                yield break;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(":"))
                continue;

            var res = JsonConvert.DeserializeObject<T>(line);

            res.Organization = headers.Organization;
            res.RequestId = headers.RequestId;
            res.ProcessingTime = headers.ProcessingTime;
            res.OpenaiVersion = headers.OpenaiVersion;
            res.Model ??= headers.Model;

            yield return res;
        }
    }

    public static async Task<HttpResponseMessage> RequestAsync(
        this HttpClient client,
        Uri uri,
        HttpMethod verb,
        object postData = null,
        bool streaming = false)
    {
        var req = new HttpRequestMessage(verb, uri);

        if (postData != null)
        {
            if (postData is HttpContent httpData)
                req.Content = httpData;
            else
            {
                var jsonContent = JsonConvert.SerializeObject(postData,
                                                              new JsonSerializerSettings
                                                              {
                                                                  NullValueHandling = NullValueHandling.Ignore
                                                              });
                req.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            }
        }

        var response = await client.SendAsync(req,
                                              streaming ?
                                                  HttpCompletionOption.ResponseHeadersRead :
                                                  HttpCompletionOption.ResponseContentRead);

        if (response.IsSuccessStatusCode)
            return response;

        string resultAsString;
        ApiError parsedError = null;

        try
        {
            resultAsString = await response.Content.ReadAsStringAsync();

            try
            {
                if (JObject.Parse(resultAsString) is { } jObj && jObj["error"] is { } jErrObj)
                    parsedError = jErrObj.ToObject<ApiError>();
            }
            catch (Exception ex)
            {
                typeof(HttpClientExtensions).GetLogger().LogError(ex, "Parsing ApiError");
            }
        }
        catch (Exception e)
        {
            resultAsString = "Additionally, the following error was thrown when attemping to read the response content: " + e.Message;
        }

        var telemetryMessage = GetErrorMessage(resultAsString, response, uri.ToString());

        typeof(HttpClientExtensions).GetLogger().LogError(telemetryMessage);

        if (parsedError is { Code: "invalid_api_key" })
            throw new OpenAiInvalidApiKeyException(parsedError.Message);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new OpenAiApiAuthException(
                parsedError?.Message ?? "OpenAI rejected the authorization with the given API key."),
            HttpStatusCode.TooManyRequests => new OpenAiApiQuotaException(
                parsedError?.Message ?? "OpenAI refused to process the request due to a rate limit."),
            HttpStatusCode.InternalServerError => new OpenAiApiException(
                parsedError?.Message ?? "OpenAI had an internal server error, which can happen occasionally. Please retry your request."),
            _ => new HttpRequestException(GetErrorMessage(resultAsString, response, uri.ToString()))
        };
    }

    private static string GetErrorMessage(string resultAsString, HttpResponseMessage response, string name, string description = "") =>
        $"Error at {name} ({description}) with HTTP status code: {response.StatusCode}. Content:\r\n{resultAsString ?? "<no content>"}";
}