using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorldAgent.Core
{
    public sealed class AiModelCatalogResult
    {
        public IReadOnlyList<string> ModelIds { get; }
        public string Endpoint { get; }

        public AiModelCatalogResult(IReadOnlyList<string> modelIds, string endpoint)
        {
            ModelIds = modelIds;
            Endpoint = endpoint;
        }
    }

    public static class AiModelCatalog
    {
        public static async Task<AiModelCatalogResult> FetchAsync(string provider, string apiBaseUrl, string apiKey)
        {
            var config = AiGatewayConfig.FromSettings(provider, apiBaseUrl, apiKey, null);
            var endpoint = AiGatewayUrl.BuildModelsEndpoint(config.Provider, apiBaseUrl);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("RimWorldAgent/1.0");

            if (config.Provider == "anthropic")
            {
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                if (!string.IsNullOrEmpty(config.ApiKey))
                    request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
            }
            else if (!string.IsNullOrEmpty(config.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            }

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"获取模型列表失败: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var modelIds = ParseModelIds(json);
            if (modelIds.Count == 0)
                throw new InvalidOperationException("接口返回成功，但没有解析到模型 ID");

            return new AiModelCatalogResult(modelIds, endpoint);
        }

        public static string BuildModelsEndpoint(string provider, string apiBaseUrl)
        {
            return AiGatewayUrl.BuildModelsEndpoint(provider, apiBaseUrl);
        }

        public static List<string> ParseModelIds(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var ids = new List<string>();
            CollectModelIds(doc.RootElement, ids);
            return ids
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void CollectModelIds(JsonElement element, List<string> ids)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        CollectModelIds(item, ids);
                    break;
                case JsonValueKind.Object:
                    if (TryAddStringProperty(element, "id", ids)) return;
                    if (TryAddStringProperty(element, "name", ids)) return;
                    if (TryAddStringProperty(element, "model", ids)) return;
                    if (element.TryGetProperty("data", out var data)) CollectModelIds(data, ids);
                    if (element.TryGetProperty("models", out var models)) CollectModelIds(models, ids);
                    break;
                case JsonValueKind.String:
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) ids.Add(value!);
                    break;
            }
        }

        private static bool TryAddStringProperty(JsonElement element, string name, List<string> ids)
        {
            if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
                return false;
            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value)) return false;
            ids.Add(value!);
            return true;
        }
    }
}
