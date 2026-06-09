using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimWorldAgent.Core;
using RimWorldAgent.Core.AgentRuntime;
using RimWorldAgent.Core.CcbManager;
using UnityEngine;
using Verse;

namespace RimWorldAgent
{
    public class RimWorldAgentMod : Mod
    {
        public static RimWorldAgentMod Instance { get; private set; } = null!;
        public AgentModSettings Settings { get; private set; }
        private Vector2 _scrollPos;
        private Task<AiModelCatalogResult>? _modelFetchTask;
        private string _modelFetchProvider = "";
        private string _modelFetchBaseUrl = "";
        private string _modelFetchStatus = "";
        private bool _modelFetchFailed;
        private string _gatewayApplyStatus = "";
        private bool _gatewayApplyFailed;

        public RimWorldAgentMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<AgentModSettings>();
        }

        public override string SettingsCategory() => "RimWorld Agent";

        private static void DrawSectionHeader(Listing_Standard listing, string title)
        {
            listing.Gap(4f);
            var rect = listing.GetRect(22f);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 10f, rect.width, 1f),
                new Color(0.25f, 0.25f, 0.3f, 0.6f));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.45f, 0.5f, 0.6f, 1f);
            Widgets.Label(new Rect(rect.x, rect.y + 2f, rect.width, 18f), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(2f);
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ApplyCompletedModelFetch();

            var h = 1280f;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, h);
            Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            if (Find.CurrentMap != null)
            {
                GUI.color = Color.yellow;
                listing.Label("端口/目录等需重载存档生效；AI 网关可用下方按钮游戏内热切换。");
                GUI.color = Color.white;
                listing.Gap(8f);
            }

            // ==================== MCP 服务 ====================
            DrawSectionHeader(listing, "MCP 服务");

            listing.Label("游戏 MCP 服务地址");
            Settings.GameMcpHost = listing.TextEntry(Settings.GameMcpHost);

            listing.Label("游戏 MCP 端口");
            var gamePortStr = listing.TextEntry(Settings.GameMcpPort.ToString());
            if (int.TryParse(gamePortStr, out int gamePort) && gamePort > 0 && gamePort <= 65535)
                Settings.GameMcpPort = gamePort;

            listing.Label("Agent MCP 端口 (SDK 连接)");
            var agentPortStr = listing.TextEntry(Settings.AgentMcpPort.ToString());
            if (int.TryParse(agentPortStr, out int agentPort) && agentPort > 0 && agentPort <= 65535)
                Settings.AgentMcpPort = agentPort;

            // ==================== 模型与思考 ====================
            DrawSectionHeader(listing, "模型与思考");

            var providerLabels = new[] { "claude-sdk (Claude Agent SDK)", "anthropic (Anthropic API)", "openai-compatible (自定义 /v1)", "openai (OpenAI 官方)" };
            var providerValues = new[] { "claude-sdk", "anthropic", "openai-compatible", "openai" };
            var providerIdx = Array.IndexOf(providerValues, Settings.AiProvider);
            if (providerIdx < 0) providerIdx = 0;
            if (listing.ButtonText($"AI 网关: {providerLabels[providerIdx]}"))
            {
                providerIdx = (providerIdx + 1) % providerValues.Length;
                Settings.AiProvider = providerValues[providerIdx];
            }

            listing.Label("API 地址 (留空使用 provider 默认)");
            Settings.ApiBaseUrl = listing.TextEntry(Settings.ApiBaseUrl);

            listing.Label("API Key (本机明文保存)");
            Settings.ApiKey = listing.TextEntry(Settings.ApiKey);
            if (!string.IsNullOrEmpty(Settings.ApiKey) && listing.ButtonText("清空 API Key"))
                Settings.ApiKey = "";

            listing.Label("模型名称 (如 claude-sonnet-4-6 / gpt-4o-mini)");
            Settings.ModelName = listing.TextEntry(Settings.ModelName);
            DrawModelCatalogControls(listing);

            if (listing.ButtonText("应用 AI 网关设置（运行时热切换）"))
                ApplyGatewayHotSwap();
            if (!string.IsNullOrEmpty(_gatewayApplyStatus))
            {
                GUI.color = _gatewayApplyFailed ? new Color(1f, 0.45f, 0.4f, 1f) : new Color(0.6f, 0.75f, 0.6f, 1f);
                listing.Label(_gatewayApplyStatus);
                GUI.color = Color.white;
            }

            var modeLabels = new[] { "adaptive (引导深度)", "disabled (禁用思考)" };
            var modeValues = new[] { "adaptive", "disabled" };
            var modeIdx = Array.IndexOf(modeValues, Settings.ThinkingMode);
            if (modeIdx < 0) modeIdx = 0;
            if (listing.ButtonText($"思考模式: {modeLabels[modeIdx]}"))
            {
                modeIdx = (modeIdx + 1) % modeValues.Length;
                Settings.ThinkingMode = modeValues[modeIdx];
            }

            listing.Gap(4f);
            var effortLabels = new[] { "low (低)", "medium (中)", "high (高)", "xhigh (极高)", "max (最大)" };
            var effortValues = new[] { "low", "medium", "high", "xhigh", "max" };
            var effortIdx = Array.IndexOf(effortValues, Settings.ThinkingEffort);
            if (effortIdx < 0) effortIdx = 2; // 默认 "high"
            if (listing.ButtonText($"思考力度: {effortLabels[effortIdx]}"))
            {
                effortIdx = (effortIdx + 1) % effortValues.Length;
                Settings.ThinkingEffort = effortValues[effortIdx];
            }

            // ==================== Token 预算 ====================
            DrawSectionHeader(listing, "Token 预算");

            listing.Label("预算上限 (K, 0=不限制)");
            var limitKStr = listing.TextEntry((Settings.TokenBudgetLimit / 1000).ToString());
            if (long.TryParse(limitKStr, out long limitK) && limitK >= 0)
                Settings.TokenBudgetLimit = limitK * 1000;

            var actionLabels = new[] { "Block (阻止)", "Warn (警告)" };
            var actionValues = new[] { "Block", "Warn" };
            var actionIdx = Array.IndexOf(actionValues, Settings.TokenBudgetAction);
            if (actionIdx < 0) actionIdx = 0;
            if (listing.ButtonText($"超出行为: {actionLabels[actionIdx]}"))
            {
                actionIdx = (actionIdx + 1) % actionValues.Length;
                Settings.TokenBudgetAction = actionValues[actionIdx];
            }

            listing.Gap(4f);
            var usage = TokenUsageTracker.GetCompactDisplay(Settings.TokenBudgetLimit);
            GUI.color = new Color(0.6f, 0.65f, 0.75f, 1f);
            listing.Label($"累计: {usage}");
            GUI.color = Color.white;

            // ==================== Agent 行为 ====================
            DrawSectionHeader(listing, "Agent 行为");

            listing.CheckboxLabeled("自动运行 Agent", ref Settings.AgentAutoRun,
                "开启后加载存档时自动启动。");

            var speedLabels = new[] { "paused (暂停)", "normal (1x)", "fast (2x)", "superfast (3x)", "ultrafast (最快)" };
            var speedValues = new[] { "paused", "normal", "fast", "superfast", "ultrafast" };
            var speedIdx = Array.IndexOf(speedValues, Settings.PlanSpeed);
            if (speedIdx < 0) speedIdx = 0;
            if (listing.ButtonText($"Plan 阶段速度: {speedLabels[speedIdx]}"))
            {
                speedIdx = (speedIdx + 1) % speedValues.Length;
                Settings.PlanSpeed = speedValues[speedIdx];
            }

            listing.Label("Skills 目录 (留空用默认)");
            Settings.SkillsDir = listing.TextEntry(Settings.SkillsDir);

            listing.Label("Project 目录 (留空用默认)");
            Settings.ProjectPath = listing.TextEntry(Settings.ProjectPath);

            // ==================== UI Bridge ====================
            DrawSectionHeader(listing, "UI 桥接 (WebSocket)");

            listing.Label("监听地址");
            Settings.BridgeHost = listing.TextEntry(Settings.BridgeHost);

            listing.Label("监听端口");
            var bpStr = listing.TextEntry(Settings.BridgePort.ToString());
            if (int.TryParse(bpStr, out int bp) && bp > 0 && bp <= 65535)
                Settings.BridgePort = bp;

            // ==================== CC Companion ====================
            DrawSectionHeader(listing, "CC Companion 依赖");

            var asmDir = System.IO.Path.GetDirectoryName(typeof(RimWorldAgentMod).Assembly.Location) ?? ".";
            var ccDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(asmDir, "cc-companion"));

            var installed = CompanionInstaller.IsInstalled(ccDir);
            var installing = CompanionInstaller.IsInstalling;
            var status = CompanionInstaller.InstallStatus;

            if (installing)
            {
                listing.Label("  状态: 安装中...");
                if (!string.IsNullOrEmpty(status)) listing.Label($"    {status}");
            }
            else if (installed)
            {
                listing.Label("  状态: 已安装 (node_modules 就绪)");
                if (listing.ButtonText("  重新安装 (npm install)"))
                    CompanionInstaller.Install(ccDir);
                if (listing.ButtonText("  卸载 (删除 node_modules)"))
                    CompanionInstaller.Uninstall(ccDir);
            }
            else
            {
                listing.Label($"  状态: 未安装{(string.IsNullOrEmpty(status) ? "" : $" ({status})")}");
                if (!installing && listing.ButtonText("  安装 (npm install)"))
                    CompanionInstaller.Install(ccDir);
            }

            listing.CheckboxLabeled("自动安装 (加载时)", ref Settings.CcbAutoInstall,
                "开启后自动检查 cc-companion/node_modules，缺失则运行 npm install。");

            DrawSectionHeader(listing, "日志");

            listing.CheckboxLabeled("☐ SDK 交互日志 (sdk-log.txt)", ref Settings.LogSdkMessages,
                "开启后 companion 将 SDK 双向通信记录写入 project 目录下的 sdk-log.txt。");
            listing.CheckboxLabeled("☐ C#↔CCB WS 日志 (ccb-ws-log.txt)", ref Settings.LogCcbWsMessages,
                "开启后 C# 将 WebSocket 收发 JSON 记录写入 project 目录下的 ccb-ws-log.txt。");

            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawModelCatalogControls(Listing_Standard listing)
        {
            var fetching = _modelFetchTask != null && !_modelFetchTask.IsCompleted;
            var cachedCount = Settings.CachedModelIds?.Count ?? 0;

            if (fetching)
            {
                GUI.color = new Color(0.65f, 0.75f, 1f, 1f);
                listing.Label("正在获取模型列表...");
                GUI.color = Color.white;
            }
            else if (listing.ButtonText(cachedCount > 0 ? "刷新模型列表" : "获取模型列表"))
            {
                StartModelFetch();
            }

            if (cachedCount > 0)
            {
                var cacheCurrent = IsModelCacheCurrent();
                GUI.color = cacheCurrent ? new Color(0.6f, 0.75f, 0.65f, 1f) : new Color(0.9f, 0.72f, 0.42f, 1f);
                var source = string.IsNullOrEmpty(Settings.CachedModelFetchedAt) ? "未知时间" : Settings.CachedModelFetchedAt;
                listing.Label(cacheCurrent
                    ? $"已缓存 {cachedCount} 个模型（{source}）"
                    : $"已缓存 {cachedCount} 个模型，但 provider/API 地址已变化，建议刷新。");
                GUI.color = Color.white;

                if (listing.ButtonText($"选择模型 ({cachedCount})"))
                    ShowModelSelectMenu();
            }

            if (!string.IsNullOrEmpty(_modelFetchStatus))
            {
                GUI.color = _modelFetchFailed ? new Color(1f, 0.45f, 0.4f, 1f) : new Color(0.6f, 0.65f, 0.75f, 1f);
                listing.Label(_modelFetchStatus);
                GUI.color = Color.white;
            }

            listing.Gap(4f);
        }

        private void StartModelFetch()
        {
            if (_modelFetchTask != null && !_modelFetchTask.IsCompleted) return;

            var provider = Settings.AiProvider;
            var baseUrl = Settings.ApiBaseUrl;
            var apiKey = Settings.ApiKey;

            _modelFetchProvider = provider;
            _modelFetchBaseUrl = baseUrl;
            _modelFetchFailed = false;
            _modelFetchStatus = "正在通过 API 地址读取 /models...";
            _modelFetchTask = Task.Run(() => AiModelCatalog.FetchAsync(provider, baseUrl, apiKey));
        }

        private void ApplyCompletedModelFetch()
        {
            var task = _modelFetchTask;
            if (task == null || !task.IsCompleted) return;

            _modelFetchTask = null;
            if (task.IsCanceled)
            {
                _modelFetchFailed = true;
                _modelFetchStatus = "获取模型列表已取消。";
                return;
            }

            if (task.IsFaulted)
            {
                _modelFetchFailed = true;
                _modelFetchStatus = task.Exception?.GetBaseException().Message ?? "获取模型列表失败。";
                return;
            }

            var result = task.Result;
            Settings.CachedModelIds = result.ModelIds.ToList();
            Settings.CachedModelProvider = _modelFetchProvider;
            Settings.CachedModelBaseUrl = _modelFetchBaseUrl;
            Settings.CachedModelFetchedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            _modelFetchFailed = false;
            _modelFetchStatus = $"已从 {result.Endpoint} 获取 {Settings.CachedModelIds.Count} 个模型。";
            if (string.IsNullOrWhiteSpace(Settings.ModelName) && Settings.CachedModelIds.Count == 1)
                Settings.ModelName = Settings.CachedModelIds[0];
            WriteSettings();
        }

        private void ShowModelSelectMenu()
        {
            var models = Settings.CachedModelIds ?? new List<string>();
            if (models.Count == 0) return;

            var options = new List<FloatMenuOption>();
            foreach (var model in models)
            {
                var modelId = model;
                var label = string.Equals(modelId, Settings.ModelName, StringComparison.Ordinal)
                    ? $"[当前] {modelId}"
                    : modelId;
                options.Add(new FloatMenuOption(label, () => SelectModel(modelId)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SelectModel(string modelId)
        {
            Settings.ModelName = modelId;
            _modelFetchFailed = false;
            _modelFetchStatus = $"已选择模型: {modelId}";
            WriteSettings();
        }

        private async void ApplyGatewayHotSwap()
        {
            WriteSettings();
            var engine = AgentEngine.Current;
            if (engine == null)
            {
                _gatewayApplyFailed = false;
                _gatewayApplyStatus = "Agent 未运行：设置已保存，加载存档后生效。";
                return;
            }
            _gatewayApplyFailed = false;
            _gatewayApplyStatus = "正在应用网关设置...";
            try
            {
                var cfg = AiGatewayConfig.FromSettings(Settings.AiProvider, Settings.ApiBaseUrl, Settings.ApiKey, Settings.ModelName);
                var (ok, message) = await engine.ApplyAiGatewayAsync(cfg);
                _gatewayApplyFailed = !ok;
                _gatewayApplyStatus = message;
            }
            catch (Exception ex)
            {
                _gatewayApplyFailed = true;
                _gatewayApplyStatus = $"应用失败: {ex.Message}";
            }
        }

        private bool IsModelCacheCurrent()
        {
            var provider = Settings.AiProvider;
            return string.Equals(Settings.CachedModelProvider, provider, StringComparison.Ordinal)
                && string.Equals(
                    AiGatewayUrl.NormalizeRootSafe(provider, Settings.CachedModelBaseUrl),
                    AiGatewayUrl.NormalizeRootSafe(provider, Settings.ApiBaseUrl),
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
