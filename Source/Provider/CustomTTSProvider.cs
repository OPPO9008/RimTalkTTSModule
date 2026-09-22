using System.Threading;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;

namespace RimTalk.TTS.Provider
{
    /// <summary>
    /// ITTSProvider implementation for user-defined custom TTS providers.
    /// Uses OpenAI-compatible format and delegates to CustomTTSClient.
    /// </summary>
    public class CustomTTSProvider : ITTSProvider
    {
        private readonly CustomProviderConfig _config;

        public CustomTTSProvider(CustomProviderConfig config)
        {
            _config = config;
            if (config != null)
            {
                TTSLog.Message($"[RimTalk.TTS] CustomTTSProvider initialized: {config.GetDisplayName()} ({config.GetFullUrl()})");
            }
        }

        public CustomProviderConfig Config => _config;

        public async Task<byte[]> GenerateSpeechAsync(TTSRequest request, CancellationToken cancellationToken = default)
        {
            if (_config == null)
            {
                TTSLog.Error("[RimTalk.TTS] CustomTTSProvider: No config set");
                return null;
            }

            // Use the provider-specific API key if provided in config, fallback to request API key
            if (!string.IsNullOrWhiteSpace(_config.ApiKey) && string.IsNullOrWhiteSpace(request.ApiKey))
            {
                request.ApiKey = _config.ApiKey;
            }

            // Use config model if request model is empty
            if (string.IsNullOrWhiteSpace(request.Model))
            {
                request.Model = _config.Model;
            }

            return await CustomTTSClient.GenerateSpeechAsync(request, _config, cancellationToken);
        }

        public void Shutdown()
        {
            // No cleanup needed for HTTP-based providers
        }

        public bool IsApiKeyValid(string apiKey)
        {
            if (!_config.RequiresApiKey) return true;
            
            // Check config-level API key first, then parameter
            if (!string.IsNullOrWhiteSpace(_config.ApiKey)) return true;
            return !string.IsNullOrWhiteSpace(apiKey);
        }
    }
}
