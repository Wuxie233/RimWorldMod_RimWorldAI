using System;

namespace RimWorldAgent.Core
{
    public class AiGatewayConfig
    {
        public string Provider { get; }
        public string ApiBaseUrl { get; }
        public string ApiKey { get; }
        public string ModelName { get; }

        private AiGatewayConfig(string provider, string apiBaseUrl, string apiKey, string modelName)
        {
            Provider = provider;
            ApiBaseUrl = apiBaseUrl;
            ApiKey = apiKey;
            ModelName = modelName;
        }

        public static AiGatewayConfig FromSettings(string? provider, string? apiBaseUrl, string? apiKey, string? modelName)
        {
            var normalizedProvider = NormalizeProvider(provider);
            var rawBaseUrl = (apiBaseUrl ?? "").Trim();
            var normalizedBaseUrl = rawBaseUrl.Length == 0
                ? ""
                : AiGatewayUrl.BuildSdkBaseUrl(normalizedProvider, rawBaseUrl);
            var normalizedApiKey = (apiKey ?? "").Trim();
            var normalizedModel = (modelName ?? "").Trim();

            return new AiGatewayConfig(normalizedProvider, normalizedBaseUrl, normalizedApiKey, normalizedModel);
        }

        private static string NormalizeProvider(string? provider)
        {
            var value = (provider ?? "claude-sdk").Trim().ToLowerInvariant();
            switch (value)
            {
                case "claude":
                case "claude-sdk":
                    return "claude-sdk";
                case "anthropic":
                    return "anthropic";
                case "openai":
                    return "openai";
                case "compatible":
                case "openai_compatible":
                case "openai-compatible":
                    return "openai-compatible";
                default:
                    throw new ArgumentException($"不支持的 AI provider: {provider}");
            }
        }
    }
}
