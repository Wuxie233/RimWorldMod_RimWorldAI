using System;

namespace RimWorldAgent.Core
{
    /// <summary>
    /// 统一的 AI 网关 Base URL 规范化：模型列表获取与运行时 SDK 共用同一规则。
    /// 末尾有无 '/'、有无 '/v1'、是否带 scheme 都会静默补全；provider 感知：
    /// openai / openai-compatible 的 SDK baseURL 含 '/v1'，anthropic 不含（SDK 自补 /v1/messages）；
    /// 模型列表端点统一为 {root}/v1/models。
    /// </summary>
    public static class AiGatewayUrl
    {
        /// <summary>provider 的默认根地址（不含 /v1、不含尾斜杠）。无默认返回空串。</summary>
        public static string DefaultRoot(string provider)
        {
            switch (provider)
            {
                case "openai":
                    return "https://api.openai.com";
                case "anthropic":
                    return "https://api.anthropic.com";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 非抛版本：把任意写法归一化为 canonical root（scheme://authority[/subpath]，无尾斜杠、无末尾 /v1 与 /models）。
        /// 输入为空时回退到 provider 默认 root；无默认或地址非法时返回 false。
        /// </summary>
        public static bool TryNormalizeRoot(string provider, string? rawInput, out string root)
        {
            root = "";
            var input = (rawInput ?? "").Trim();
            if (input.Length == 0)
            {
                root = DefaultRoot(provider);
                return root.Length > 0;
            }

            if (input.IndexOf("://", StringComparison.Ordinal) < 0)
                input = "https://" + input;

            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;

            var path = uri.AbsolutePath;
            while (path.Contains("//"))
                path = path.Replace("//", "/");
            path = path.TrimEnd('/');

            // 依次剥掉末尾的 /models 与 /v1（大小写不敏感），得到干净根路径
            path = StripTrailingSegment(path, "models");
            path = StripTrailingSegment(path, "v1");

            root = (uri.Scheme + "://" + uri.Authority + path).TrimEnd('/');
            return true;
        }

        /// <summary>抛出版本：归一化失败时给出中文诊断（用于真正需要 base url 的场景）。</summary>
        public static string NormalizeRoot(string provider, string? rawInput)
        {
            if (TryNormalizeRoot(provider, rawInput, out var root))
                return root;

            var raw = (rawInput ?? "").Trim();
            if (raw.Length == 0)
                throw new InvalidOperationException(
                    provider == "openai-compatible"
                        ? "openai-compatible 需要先填写 API 地址"
                        : $"当前 provider 不支持自动获取模型列表: {provider}");

            throw new ArgumentException($"API 地址无效: {rawInput}");
        }

        /// <summary>比较/展示用：永不抛，失败时回退到 trim + 去尾斜杠。</summary>
        public static string NormalizeRootSafe(string provider, string? rawInput)
        {
            return TryNormalizeRoot(provider, rawInput, out var root)
                ? root
                : (rawInput ?? "").Trim().TrimEnd('/');
        }

        /// <summary>运行时交给 SDK 的 baseURL：openai/openai-compatible 含 /v1，anthropic 不含。</summary>
        public static string BuildSdkBaseUrl(string provider, string? rawInput)
        {
            var root = NormalizeRoot(provider, rawInput);
            var query = ExtractQuery(rawInput);
            switch (provider)
            {
                case "openai":
                case "openai-compatible":
                    return root + "/v1" + query;
                default:
                    return root + query;
            }
        }

        /// <summary>模型列表端点：统一 {root}/v1/models（claude-sdk 不支持）。</summary>
        public static string BuildModelsEndpoint(string provider, string? rawInput)
        {
            if (provider == "claude-sdk")
                throw new InvalidOperationException("claude-sdk 不支持通过 Base URL 获取模型列表；请切到 anthropic / openai-compatible / openai，或手填模型名。");

            var root = NormalizeRoot(provider, rawInput);
            var query = ExtractQuery(rawInput);
            return root + "/v1/models" + query;
        }

        private static string StripTrailingSegment(string path, string segment)
        {
            var idx = path.LastIndexOf('/');
            if (idx < 0)
                return path;
            var last = path.Substring(idx + 1);
            return string.Equals(last, segment, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, idx)
                : path;
        }

        private static string ExtractQuery(string? rawInput)
        {
            var input = (rawInput ?? "").Trim();
            if (input.Length == 0)
                return "";
            if (input.IndexOf("://", StringComparison.Ordinal) < 0)
                input = "https://" + input;
            return Uri.TryCreate(input, UriKind.Absolute, out var uri) ? uri.Query : "";
        }
    }
}
