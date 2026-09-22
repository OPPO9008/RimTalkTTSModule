using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.Util;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.TTS.Service
{
    //给 TTSService 返回结构化数据的辅助类
    public class PreProcessResult
    {
        public string Text;
        public string Emotion;
    }

    [Serializable]
    public class PreProcessResultJson
    {
        public string text;
        public string emotion;
    }
    /// <summary>
    /// Simple LLM client for TTS text processing - no role concept, just plain text in/out
    /// </summary>
    public static class InputPreProcessClient
    {
        /// <summary>
        /// Get base URL for the configured provider
        /// </summary>
        private static string GetBaseUrl(TTSSettings settings)
        {
            return settings.ApiProvider switch
            {
                TTSApiProvider.DeepSeek => "https://api.deepseek.com",
                TTSApiProvider.OpenAI => "https://api.openai.com",
                TTSApiProvider.Custom => settings.CustomBaseUrl,
                _ => ""
            };
        }

        /// <summary>
        /// Resolve base URL from RimTalk's active AI config.
        /// Google/Gemini uses OpenAI-compatible endpoint.
        /// </summary>
        private static string GetRimTalkBaseUrl(ApiConfig config)
        {
            if (config == null) return "";

            switch (config.Provider)
            {
                case AIProvider.Google:
                    // Use Gemini's OpenAI-compatible endpoint (full path since SendHttpRequestAsync expects it)
                    return "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
                case AIProvider.Local:
                case AIProvider.Custom:
                    return config.BaseUrl ?? "";
                default:
                    // Standard providers: use their registered endpoint, strip /chat/completions suffix
                    var endpointUrl = config.Provider.GetEndpointUrl();
                    if (string.IsNullOrEmpty(endpointUrl)) return "";
                    // The endpoint URLs in registry already include /v1/chat/completions;
                    // we need just the base, since SendHttpRequestAsync appends the path
                    int idx = endpointUrl.IndexOf("/v1/chat/completions");
                    if (idx > 0) return endpointUrl.Substring(0, idx);
                    idx = endpointUrl.IndexOf("/chat/completions");
                    if (idx > 0) return endpointUrl.Substring(0, idx);
                    return endpointUrl;
            }
        }

        /// <summary>
        /// Send a simple text query and get text response (no role/conversation context)
        /// </summary>
        public static async Task<(PreProcessResult response, bool success)> QueryAsync(string prompt, string text, TTSSettings settings)
        {
            if (settings == null)
            {
                TTSLog.Warning("[RimTalk.TTS] SimpleLLMClient: settings is null");
                return (null, false);
            }

            string baseUrl;
            string apiKey;
            string model;

            if (settings.ApiProvider == TTSApiProvider.RimTalkSame)
            {
                // Resolve from RimTalk's active config
                var rimTalkConfig = Settings.Get()?.GetActiveConfig();
                if (rimTalkConfig == null)
                {
                    TTSLog.Warning("[RimTalk.TTS] SimpleLLMClient: RimTalk has no active API config");
                    return (null, false);
                }

                baseUrl = GetRimTalkBaseUrl(rimTalkConfig);
                apiKey = rimTalkConfig.ApiKey;
                model = rimTalkConfig.SelectedModel == "Custom"
                    ? rimTalkConfig.CustomModelName
                    : rimTalkConfig.SelectedModel;

                TTSLog.Message($"[RimTalk.TTS] Using RimTalk config: provider={rimTalkConfig.Provider}, model={model}");
            }
            else
            {
                baseUrl = GetBaseUrl(settings);
                apiKey = settings.ApiKey;
                model = settings.Model;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                TTSLog.Warning("[RimTalk.TTS] SimpleLLMClient: API key not configured");
                return (null, false);
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                TTSLog.Warning("[RimTalk.TTS] SimpleLLMClient: Model not configured");
                return (null, false);
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                TTSLog.Warning("[RimTalk.TTS] Empty prompt provided to SimpleLLMClient");
                return (null, false);
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                TTSLog.Warning("[RimTalk.TTS] SimpleLLMClient: Base URL is empty");
                return (null, false);
            }

            try
            {
                if (settings.RemoveBracketsInPreProcess)
                    text = RemoveBrackets(text);

                // Build simple OpenAI-compatible request with single user message
                string jsonRequest = BuildRequest(prompt, text, model);

                TTSLog.Message($"[RimTalk.TTS] Sending LLM request to {settings.ApiProvider}: {prompt}");

                // Send HTTP request
                var (responseJson, success) = await SendHttpRequestAsync(jsonRequest, baseUrl, apiKey);

                if (!success)
                {
                    TTSLog.Warning("[RimTalk.TTS] LLM HTTP request failed");
                    return (null, false);
                }

                if (string.IsNullOrEmpty(responseJson))
                {
                    TTSLog.Warning("[RimTalk.TTS] LLM returned empty response");
                    return (null, false);
                }

                // Extract structured PreProcessResult from response
                PreProcessResult result = ExtractContentFromResponse(responseJson);

                if (result == null)
                {
                    TTSLog.Warning("[RimTalk.TTS] Failed to extract content from LLM response");
                    return (null, false);
                }

                return (result, true);
            }
            catch (Exception ex)
            {
                TTSLog.Error($"[RimTalk.TTS] SimpleLLMClient.QueryAsync error: {ex.Message}\n{ex.StackTrace}");
                return (null, false);
            }
        }

        private static string BuildRequest(string prompt, string text, string model)
        {
            var escapedPrompt = prompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            var escapedText = text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            return $@"{{
  ""model"": ""{model}"",
  ""messages"": [
    {{
      ""role"": ""system"",
      ""content"": ""{escapedPrompt}""
    }},
    {{
      ""role"": ""user"",
      ""content"": ""{escapedText}""
    }}
  ],
  ""temperature"": 0.65,
  ""top_p"": 0.9,
  ""response_format"": {{
    ""type"": ""json_object""
  }}
}}";
        }

        private static async Task<(string response, bool success)> SendHttpRequestAsync(string jsonContent, string baseUrl, string apiKey)
        {
            // OpenAI-compatible endpoint format
            baseUrl = baseUrl?.Trim().TrimEnd('/');
            string endpoint;
            if (baseUrl.Contains("/chat/completions"))
            {
                endpoint = baseUrl;
            }
            else if (baseUrl.EndsWith("/v1"))
            {
                endpoint = baseUrl + "/chat/completions";
            }
            else
            {
                endpoint = baseUrl + "/v1/chat/completions";
            }

            try
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

                using var webRequest = new UnityWebRequest(endpoint, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");

                // OpenAI-compatible: Bearer token
                if (!string.IsNullOrEmpty(apiKey))
                {
                    webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                }

                var asyncOperation = webRequest.SendWebRequest();

                // Wait for completion
                while (!asyncOperation.isDone)
                {
                    if (Current.Game == null) return (null, false);
                    await Task.Delay(250);
                }

                // Check for errors
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    TTSLog.Error($"[RimTalk.TTS] HTTP request failed: {webRequest.responseCode} {webRequest.error}");
                    TTSLog.Error($"[RimTalk.TTS] Response: {webRequest.downloadHandler?.text}");
                    return (null, false);
                }

                string responseText = webRequest.downloadHandler.text;
                TTSLog.Message($"[RimTalk.TTS] HTTP response received: {responseText}");
                return (responseText, true);
            }
            catch (Exception ex)
            {
                TTSLog.Error($"[RimTalk.TTS] HTTP request error: {ex.Message}\n{ex.StackTrace}");
                return ("", false);
            }
        }

        private static PreProcessResult ExtractContentFromResponse(string jsonResponse)
        {
            try
            {
                // Simple JSON parsing to extract content field
                // Looking for: "choices":[{"message":{"content":"..."}}]
                
                int contentIndex = jsonResponse.IndexOf("\"content\"");
                if (contentIndex == -1) return null;

                int startQuote = jsonResponse.IndexOf("\"", contentIndex + 9);
                if (startQuote == -1) return null;
                
                startQuote++; // Move past the opening quote
                
                // Find the closing quote, handling escaped quotes
                int endQuote = startQuote;
                while (endQuote < jsonResponse.Length)
                {
                    if (jsonResponse[endQuote] == '\"' && jsonResponse[endQuote - 1] != '\\')
                    {
                        break;
                    }
                    endQuote++;
                }

                if (endQuote >= jsonResponse.Length) return null;

                string content = jsonResponse.Substring(startQuote, endQuote - startQuote);

                // Unescape JSON string
                content = content
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");

                var parsed = JsonUtil.DeserializeFromJson<PreProcessResultJson>(content);
                return new PreProcessResult { Text = parsed.text, Emotion = parsed.emotion };
            }
            catch (Exception ex)
            {
                TTSLog.Error($"[RimTalk.TTS] Failed to parse JSON response: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Remove content within various bracket types and replace with ellipsis
        /// </summary>
        private static string RemoveBrackets(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = Regex.Replace(text, @"\([^()]*\)", "...");  // (content)
            text = Regex.Replace(text, @"\uff08[^\uff08\uff09]*\uff09", "...");  // （content）full-width
            text = Regex.Replace(text, @"\[[^\[\]]*\]", "...");  // [content]
            text = Regex.Replace(text, @"\u3010[^\u3010\u3011]*\u3011", "...");  // 【content】full-width
            text = Regex.Replace(text, @"\*[^*]*\*", "...");  // *content*
            text = Regex.Replace(text, @"<[^<>]*>", "...");  // <content>
            text = Regex.Replace(text, @"/[^/]*/", "...");  // /content/
            text = Regex.Replace(text, @"\\[^\\]*\\", "...");  // \content\
            text = Regex.Replace(text, @"#[^#]*#", "...");  // #content#

            return text;
        }
    }
}
