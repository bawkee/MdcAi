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