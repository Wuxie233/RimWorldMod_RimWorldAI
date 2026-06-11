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
        private float _settingsContentHeight;
        private readonly Dictionary<string, bool> _sectionExpanded = new Dictionary<string, bool>();
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

        /// <summary>下拉选择行：点按钮弹 FloatMenu 列出全部选项，当前项打勾。替代"点一下切下一个"的循环按钮。</summary>
        private void DropdownRow(Listing_Standard listing, string label, string current,
            (string val, string disp)[] options, Action<string> onSelect)
        {
            var match = options.FirstOrDefault(o => o.val == current);
            var disp = string.IsNullOrEmpty(match.disp) ? current : match.disp;
            if (listing.ButtonText($"{label}：{disp}"))
            {
                var menu = options
                    .Select(o => new FloatMenuOption((o.val == current ? "✓ " : "    ") + o.disp, () => onSelect(o.val)))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(menu));
            }
        }

        /// <summary>可折叠分组头：返回是否展开；点击切换展开/收起，状态按会话保留。</summary>
        private bool Section(Listing_Standard listing, string title, bool defaultExpanded)
        {
            if (!_sectionExpanded.TryGetValue(title, out var expanded))
            {
                expanded = defaultExpanded;
                _sectionExpanded[title] = expanded;
            }
            listing.Gap(6f);
            var rect = listing.GetRect(26f);
            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.22f, 0.28f, 0.55f));
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, 22f), $"{(expanded ? "▼" : "▶")}  {title}");
            if (Widgets.ButtonInvisible(rect))
            {
                expanded = !expanded;
                _sectionExpanded[title] = expanded;
            }
            listing.Gap(4f);
            return expanded;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ApplyCompletedModelFetch();

            var h = _settingsContentHeight > 0f ? _settingsContentHeight : 1600f;
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, h);
            Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // 渲染护栏：任一控件抛异常不再导致整页空白；错误显示并写日志，listing 在 finally 保证收尾
            try
            {

            if (Find.CurrentMap != null)
            {
                GUI.color = new Color(0.85f, 0.78f, 0.4f, 1f);
                listing.Label("提示：端口/目录等需重载存档生效；AI 网关可用下方按钮游戏内热切换。");
                GUI.color = Color.white;
            }

            if (Section(listing, "AI 网关", true))
            {
                DropdownRow(listing, "AI 网关", Settings.AiProvider, new[]
                {
                    ("claude-sdk", "claude-sdk (Claude Agent SDK)"),
                    ("anthropic", "anthropic (Anthropic API)"),
                    ("openai", "openai (OpenAI 官方)"),
                    ("openai-compatible", "openai-compatible (自定义 /v1)"),
                }, v => Settings.AiProvider = v);

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

                listing.GapLine(8f);
                bool thinkingOn = Settings.ThinkingMode != "disabled";
                bool prevThinkingOn = thinkingOn;
                listing.CheckboxLabeled("启用思考 / 推理", ref thinkingOn,
                    "为支持的模型开启思考过程。底层按 provider 自动映射：Claude→adaptive，OpenAI→reasoning summary，Anthropic→extended thinking。");
                if (thinkingOn != prevThinkingOn)
                    Settings.ThinkingMode = thinkingOn ? "adaptive" : "disabled";
                if (thinkingOn)
                {
                    DropdownRow(listing, "思考力度", Settings.ThinkingEffort, new[]
                    {
                        ("low", "low (低)"),
                        ("medium", "medium (中)"),
                        ("high", "high (高)"),
                        ("xhigh", "xhigh (极高)"),
                        ("max", "max (最大)"),
                    }, v => Settings.ThinkingEffort = v);
                    GUI.color = new Color(0.6f, 0.65f, 0.75f, 1f);
                    listing.Label("需模型本身支持思考（Claude 3.7+ / GPT o·gpt-5 系列等）；不支持的模型会忽略或报错。");
                    GUI.color = Color.white;
                }
            }

            if (Section(listing, "Agent 行为", true))
            {
                listing.CheckboxLabeled("自动运行 Agent", ref Settings.AgentAutoRun,
                    "开启后加载存档时自动启动。");

                DropdownRow(listing, "Plan 阶段速度", Settings.PlanSpeed, new[]
                {
                    ("paused", "paused (暂停)"),
                    ("normal", "normal (1x)"),
                    ("fast", "fast (2x)"),
                    ("superfast", "superfast (3x)"),
                    ("ultrafast", "ultrafast (最快)"),
                }, v => Settings.PlanSpeed = v);

                DropdownRow(listing, "回复语言", Settings.ReplyLanguage, new[]
                {
                    ("auto", "auto (跟随用户语言)"),
                    ("zh", "中文"),
                    ("en", "English"),
                }, v => Settings.ReplyLanguage = v);

                DropdownRow(listing, "自主度", Settings.Autonomy, new[]
                {
                    ("conservative", "conservative (保守, 多确认)"),
                    ("balanced", "balanced (平衡)"),
                    ("autonomous", "autonomous (高自主, 少打扰)"),
                }, v => Settings.Autonomy = v);
            }

            if (Section(listing, "Token 预算", true))
            {
                listing.Label("预算上限 (K, 0=不限制)");
                var limitKStr = listing.TextEntry((Settings.TokenBudgetLimit / 1000).ToString());
                if (long.TryParse(limitKStr, out long limitK) && limitK >= 0)
                    Settings.TokenBudgetLimit = limitK * 1000;

                DropdownRow(listing, "超出行为", Settings.TokenBudgetAction, new[]
                {
                    ("Block", "Block (阻止)"),
                    ("Warn", "Warn (警告)"),
                }, v => Settings.TokenBudgetAction = v);

                var usage = TokenUsageTracker.GetCompactDisplay(Settings.TokenBudgetLimit);
                GUI.color = new Color(0.6f, 0.65f, 0.75f, 1f);
                listing.Label($"累计: {usage}");
                GUI.color = Color.white;
            }

            if (Section(listing, "连接与目录（高级）", false))
            {
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

                listing.Label("UI 桥接监听地址");
                Settings.BridgeHost = listing.TextEntry(Settings.BridgeHost);

                listing.Label("UI 桥接监听端口");
                var bpStr = listing.TextEntry(Settings.BridgePort.ToString());
                if (int.TryParse(bpStr, out int bp) && bp > 0 && bp <= 65535)
                    Settings.BridgePort = bp;

                listing.Label("Skills 目录 (留空用默认)");
                Settings.SkillsDir = listing.TextEntry(Settings.SkillsDir);

                listing.Label("Project 目录 (留空用默认)");
                Settings.ProjectPath = listing.TextEntry(Settings.ProjectPath);
            }

            if (Section(listing, "CC Companion 依赖（高级）", false))
            {
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
            }

            if (Section(listing, "日志（高级）", false))
            {
                listing.CheckboxLabeled("SDK 交互日志 (sdk-log.txt)", ref Settings.LogSdkMessages,
                    "开启后 companion 将 SDK 双向通信记录写入 project 目录下的 sdk-log.txt。");
                listing.CheckboxLabeled("C#↔CCB WS 日志 (ccb-ws-log.txt)", ref Settings.LogCcbWsMessages,
                    "开启后 C# 将 WebSocket 收发 JSON 记录写入 project 目录下的 ccb-ws-log.txt。");
            }

            _settingsContentHeight = listing.CurHeight + 24f;
            }
            catch (Exception ex)
            {
                GUI.color = new Color(1f, 0.45f, 0.4f, 1f);
                listing.Label($"设置页渲染异常：{ex.GetType().Name}: {ex.Message}\n（已写入日志；可点窗口右上角 X 或底部「关闭」退出）");
                GUI.color = Color.white;
                CoreLog.Error($"[settings] DoSettingsWindowContents 渲染异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                listing.End();
                Widgets.EndScrollView();
            }
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
