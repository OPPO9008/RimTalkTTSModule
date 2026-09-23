// e:\git\RimTalkTTSModule\Source\Service\Player2TTSClient.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.Util;

namespace RimTalk.TTS.Service
{
    /// <summary>
    /// HTTP client for Player2 TTS API (local app and OAuth2 device flow)
    /// </summary>
    public class Player2TTSClient
    {
        private static readonly HttpClient client = new HttpClient();

        public class VoiceInfo
        {
            public string id { get; set; }
            public string name { get; set; }
            public string language { get; set; }
            public string gender { get; set; }
        }

        public class DeviceLoginInfo
        {
            public string deviceCode { get; set; }
            public string userCode { get; set; }
            public string verificationUri { get; set; }
            public string verificationUriComplete { get; set; }
            public int expiresIn { get; set; }
            public int interval { get; set; }
        }

        public class VoicesResponse
        {
            public List<VoiceInfo> voices { get; set; }
        }

        public class KeyResponse
        {
            public string p2Key { get; set; }
        }

        public class SpeakRequestBody
        {
            public string text { get; set; }
            public List<string> voice_ids { get; set; }
            public float speed { get; set; }
            public string audio_format { get; set; }
        }

        public class SpeakResponse
        {
            public string data { get; set; }
        }

        public class DeviceLoginRequestBody
        {
            public string client_id { get; set; }
        }

        public class DeviceTokenRequestBody
        {
            public string client_id { get; set; }
            public string device_code { get; set; }
            public string grant_type { get; set; }
        }

        public static async Task<List<VoiceInfo>> GetVoicesAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/tts/voices";
                var response = await client.GetAsync(url, cancellationToken);
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    TTSLog.Error($"RimTalkTTS: Player2 get voices failed. Status: {response.StatusCode}, Error: {content}");
                    return null;
                }

                var result = JsonUtil.DeserializeFromJson<VoicesResponse>(content);
                return result?.voices ?? new List<VoiceInfo>();
            }
            catch (Exception ex)
            {
                TTSLog.Error($"RimTalkTTS: Player2 get voices error: {ex.Message}");
                return null;
            }
        }

        public static async Task<byte[]> GenerateSpeechAsync(TTSRequest request, string baseUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/tts/speak";

                float speed = request.Speed;
                if (speed < 0.25f) speed = 0.25f;
                if (speed > 4.0f) speed = 4.0f;

                var body = new SpeakRequestBody
                {
                    text = request.Input,
                    voice_ids = string.IsNullOrEmpty(request.Voice) ? null : new List<string> { request.Voice },
                    speed = speed,
                    audio_format = "wav"
                };
                string json = JsonUtil.SerializeToJson(body);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {request.ApiKey}");

                var response = await client.SendAsync(httpRequest, cancellationToken);
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    TTSLog.Error($"RimTalkTTS: Player2 speak failed. Status: {response.StatusCode}, Error: {content}");
                    return null;
                }

                var result = JsonUtil.DeserializeFromJson<SpeakResponse>(content);
                if (result == null || string.IsNullOrEmpty(result.data))
                {
                    TTSLog.Error("RimTalkTTS: Player2 speak response contains no audio data");
                    return null;
                }

                string data = result.data;
                int base64Index = data.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
                if (base64Index >= 0)
                {
                    data = data.Substring(base64Index + ";base64,".Length);
                }

                return Convert.FromBase64String(data);
            }
            catch (Exception ex)
            {
                TTSLog.Error($"RimTalkTTS: Player2 speak error: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        public static async Task<string> LoginWithLocalAppAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"{TTSConstant.Player2LocalBaseUrl}/login/web/{TTSConstant.Player2GameClientId}";

                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(10));
                    var response = await client.PostAsync(url, new StringContent(""), linkedCts.Token);
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        TTSLog.Error($"RimTalkTTS: Player2 local login failed. Status: {response.StatusCode}, Error: {content}");
                        return null;
                    }

                    var result = JsonUtil.DeserializeFromJson<KeyResponse>(content);
                    return result?.p2Key;
                }
            }
            catch (Exception ex)
            {
                TTSLog.Error($"RimTalkTTS: Player2 local login error: {ex.Message}");
                return null;
            }
        }

        public static async Task<DeviceLoginInfo> StartDeviceLoginAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/login/device/new";
                var body = new DeviceLoginRequestBody { client_id = TTSConstant.Player2GameClientId };
                string json = JsonUtil.SerializeToJson(body);

                var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
                string content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    TTSLog.Error($"RimTalkTTS: Player2 device login start failed. Status: {response.StatusCode}, Error: {content}");
                    return null;
                }

                return JsonUtil.DeserializeFromJson<DeviceLoginInfo>(content);
            }
            catch (Exception ex)
            {
                TTSLog.Error($"RimTalkTTS: Player2 device login start error: {ex.Message}");
                return null;
            }
        }

        public static async Task<string> PollDeviceTokenAsync(string baseUrl, string deviceCode, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/login/device/token";
                var body = new DeviceTokenRequestBody
                {
                    client_id = TTSConstant.Player2GameClientId,
                    device_code = deviceCode,
                    grant_type = "urn:ietf:params:oauth:grant-type:device_code"
                };
                string json = JsonUtil.SerializeToJson(body);

                var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                string content = await response.Content.ReadAsStringAsync();
                var result = JsonUtil.DeserializeFromJson<KeyResponse>(content);
                return result?.p2Key;
            }
            catch (Exception ex)
            {
                TTSLog.Warning($"RimTalkTTS: Player2 device token poll error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Preview text in the voice's own language
        /// </summary>
        public static string GetPreviewText(string language)
        {
            switch (language)
            {
                case "mandarin_chinese":
                    return "只要不失去你的崇高，整个世界都会向你敞开。";
                case "japanese":
                    return "高潔さを失わない限り、世界は必ずあなたに門戸を開く。";
                case "korean":
                    return "고결함을 잃지 않는다면 온 세상이 너에게 열릴 것이다.";
                case "spanish":
                    return "Mientras no pierdas tu nobleza, el mundo entero se abrirá ante ti.";
                case "french":
                    return "Tant que vous ne perdez pas votre noblesse, le monde entier s'ouvrira à vous.";
                case "hindi":
                    return "जब तक आप अपनी महानता नहीं खोते, पूरी दुनिया आपके लिए खुल जाएगी।";
                case "italian":
                    return "Finché non perderai la tua nobiltà, il mondo intero si aprirà a te.";
                case "brazilian_portuguese":
                    return "Desde que você não perca sua nobreza, o mundo inteiro se abrirá para você.";
                default:
                    return "As long as you don't lose your nobility, the whole world will open up to you.";
            }
        }
    }
}
