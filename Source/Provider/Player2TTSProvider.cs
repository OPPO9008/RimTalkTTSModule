// e:\git\RimTalkTTSModule\Source\Provider\Player2TTSProvider.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;

namespace RimTalk.TTS.Provider
{
    /// <summary>
    /// Player2 TTS provider
    /// </summary>
    public class Player2TTSProvider : ITTSProvider
    {
        public async Task<byte[]> GenerateSpeechAsync(TTSRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                string baseUrl = TTSConfig.Settings?.GetSupplierBaseUrl(TTSSettings.TTSSupplier.Player2TTS)
                    ?? TTSConstant.Player2LocalBaseUrl;
                return await Player2TTSClient.GenerateSpeechAsync(request, baseUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                TTSLog.Error($"RimTalkTTS: Player2TTS generation failed: {ex.Message}");
                return null;
            }
        }

        public void Shutdown()
        {
        }

        public bool IsApiKeyValid(string apiKey)
        {
            return !string.IsNullOrWhiteSpace(apiKey);
        }
    }
}
