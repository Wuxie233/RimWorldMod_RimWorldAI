using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAgent
{
    /// <summary>AI 对话窗口 — 通过 BridgeBus WS 通信，和 WebUI 共享同一数据源</summary>
    public class Dialog_AiChat : Window
    {
        private Vector2 _chatScrollPos;
        private bool _scrollToBottom;
        private string _inputText = "";
        private static float _alpha = 0.85f;
        private BridgeClient? Bridge => MapComponent_AgentUI.Bridge;
        private bool BridgeConnected => MapComponent_AgentUI.IsConnected;

        private static readonly Color UserBgColor = new Color(0.18f, 0.20f, 0.24f, 1f);
        private static readonly Color AiBgColor = new Color(0.14f, 0.16f, 0.18f, 1f);
        private static readonly Color SubagentBgColor = new Color(0.16f, 0.14f, 0.20f, 1f);
        private static readonly Color ErrorBgColor = new Color(0.24f, 0.12f, 0.12f, 1f);

        protected override float Margin => 6f;

        public Dialog_AiChat()
        {
            optionalTitle = "RimWorld AI Commander";
            doCloseX = true;
            closeOnCancel = true;
            closeOnAccept = false;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
            forcePause = false;
            layer = WindowLayer.Dialog;
            preventCameraMotion = false;
            doWindowBackground = true;
            drawShadow = true;
        }

        public override Vector2 InitialSize =>
            new Vector2(UI.screenWidth / 3f + 160f, UI.screenHeight / 3f + 80f);

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(UI.screenWidth - InitialSize.x - 10f, 10f,
                InitialSize.x, InitialSize.y);
            windowRect = windowRect.Rounded();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            ChatDisplayState.OnChanged += OnChatChanged;
            _chatUserScrolledUp = false;
            _scrollToBottom = true;
        }

        public override void PostClose()
        {
            ChatDisplayState.OnChanged -= OnChatChanged;
            base.PostClose();
        }

        private int _lastChatCount;
        private bool _chatUserScrolledUp;
        private float _lastMaxScroll = -1f;

        private void OnChatChanged()
        {
            var snap = ChatDisplayState.Snapshot;
            if (snap.Count != _lastChatCount) _scrollToBottom = true;
            _lastChatCount = snap.Count;
        }

        private async void TrySendInput()
        {
            var text = _inputText.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (Bridge == null || !Bridge.IsConnected) return;

            _inputText = "";
            ChatDisplayState.OnUserMessage(text);
            await Bridge.SendChat(text);
        }

        // ========== 主布局 ==========

        public override void DoWindowContents(Rect inRect)
        {
            ChatDisplayState.DrainEvents();

            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                TrySendInput();
                Event.current.Use();
            }

            var entries = ChatDisplayState.Snapshot;

            float headerH = 22f;
            float inputH = 28f;
            float footerH = 22f;
            float gap = 4f;

            DrawHeader(new Rect(inRect.x, inRect.y, inRect.width, headerH));

            float panelsY = inRect.y + headerH + gap;
            float panelsH = inRect.height - headerH - inputH - footerH - gap * 3;

            DrawConversationPanel(
                new Rect(inRect.x, panelsY, inRect.width, panelsH), entries);

            float inputY = panelsY + panelsH + gap;
            DrawInputRow(new Rect(inRect.x, inputY, inRect.width, inputH));

            float footerY = inputY + inputH + gap;
            DrawFooter(new Rect(inRect.x, footerY, inRect.width, footerH));
        }

        // ========== 顶栏 ==========

        private static void DrawHeader(Rect rect)
        {
            string colony = "Unknown Colony";
            try
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    var parent = map.Parent;
                    if (parent != null && !parent.Label.NullOrEmpty())
                        colony = parent.Label;
                    else if (Find.World?.info?.name != null)
                        colony = Find.World.info.name;
                }
            }
            catch { }

            string dayInfo = "";
            try
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    var season = GenLocalDate.Season(map);
                    string seasonName = season switch { Season.Spring => "Vernal", Season.Summer => "Aestival",
                        Season.Fall => "Autumn", Season.Winter => "Hibernal", _ => season.ToString() };
                    int dayOfQ = GenLocalDate.DayOfQuadrum(map);
                    int year = GenLocalDate.Year(map);
                    dayInfo = $" -- {year}y {seasonName} d{dayOfQ}";
                }
            }
            catch { }

            string model = ChatDisplayState.CurrentModel;
            if (!string.IsNullOrEmpty(model))
            {
                int slash = model.LastIndexOf('/');
                if (slash >= 0) model = model.Substring(slash + 1);
            }
            string header = $"{colony}{dayInfo}{(string.IsNullOrEmpty(model) ? "" : $" -- {model}")}";
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.55f, 0.55f, 0.55f, _alpha);
            Widgets.Label(new Rect(rect.x, rect.y + 2f, rect.width, rect.height - 2f), header);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax, rect.width, 1f),
                new Color(0.18f, 0.18f, 0.20f, _alpha));
        }

        // ========== 对话流 ==========

        private void DrawConversationPanel(Rect panelRect, List<ChatEntry> entries)
        {
            Rect scrollRect = new Rect(panelRect.x, panelRect.y + 2f,
                panelRect.width - 10f, panelRect.height - 2f);

            if (entries.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.35f, 0.35f, 0.35f, _alpha);
                Widgets.Label(new Rect(scrollRect.x, scrollRect.y + 4f,
                    scrollRect.width, 16f), "Waiting for AI response...");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }

            float contentWidth = scrollRect.width - 8f;
            float totalH = 4f;
            foreach (var entry in entries)
            {
                CalcEntryHeight(entry, contentWidth);
                totalH += entry.CachedHeight + 6f;
            }

            bool isStreaming = entries.Count > 0
                && entries[entries.Count - 1].State == ChatState.Streaming;

            float maxScroll = Mathf.Max(0f, totalH - scrollRect.height);

            if (_lastMaxScroll >= 0f)
            {
                bool wasAtBottom = _chatScrollPos.y >= _lastMaxScroll - 4f;
                if (!wasAtBottom && _chatScrollPos.y < maxScroll - 4f) _chatUserScrolledUp = true;
                if (_chatScrollPos.y >= maxScroll - 2f) _chatUserScrolledUp = false;
                if (wasAtBottom && isStreaming) _chatUserScrolledUp = false;
            }
            if (!isStreaming) { _chatUserScrolledUp = false; _lastMaxScroll = -1f; }
            else _lastMaxScroll = maxScroll;

            if ((isStreaming && !_chatUserScrolledUp) || (_scrollToBottom && !_chatUserScrolledUp))
            {
                _chatScrollPos.y = maxScroll;
                _scrollToBottom = false;
            }

            if (Event.current.type == EventType.ScrollWheel && Mouse.IsOver(scrollRect))
            {
                _chatScrollPos.y += Event.current.delta.y * 20f;
                _chatScrollPos.y = Mathf.Clamp(_chatScrollPos.y, 0f, maxScroll);
                Event.current.Use();
            }
            _chatScrollPos.y = Mathf.Clamp(_chatScrollPos.y, 0f, maxScroll);

            GUI.BeginGroup(scrollRect);
            float curY = 4f - _chatScrollPos.y;
            foreach (var entry in entries)
            {
                float entryH = entry.CachedHeight + 6f;
                if (curY + entryH > 0f && curY < scrollRect.height)
                    DrawEntry(entry, contentWidth, curY);
                curY += entryH;
            }
            GUI.EndGroup();

            if (maxScroll > 0f)
            {
                float barW = 6f;
                float barX = scrollRect.xMax - barW - 2f;
                float barH = Mathf.Max(scrollRect.height * scrollRect.height / totalH, 16f);
                float barY = scrollRect.y + _chatScrollPos.y * scrollRect.height / totalH;
                Widgets.DrawBoxSolid(new Rect(barX, barY, barW, barH),
                    new Color(0.35f, 0.35f, 0.35f, 0.6f));
            }
        }

        private static void CalcEntryHeight(ChatEntry entry, float contentWidth)
        {
            var thinking = (entry.ThinkingText ?? "").Replace("_", "__");
            var body = (entry.Text ?? "").Replace("_", "__");
            bool streaming = entry.State == ChatState.Streaming;
            bool changed = body.Length != entry.CachedTextLen
                        || thinking.Length != entry.CachedThinkingLen;
            if (!changed && entry.CachedHeight > 0f) return;

            float textAreaW = contentWidth - 32f;
            float totalH = 0f;
            if (!string.IsNullOrEmpty(thinking))
                totalH += Text.CalcHeight(thinking.StripTags(), textAreaW);
            if (!string.IsNullOrEmpty(body))
                totalH += Text.CalcHeight(body.StripTags(), textAreaW);

            float newH = 25f + Mathf.Max(totalH, 10f);
            if (streaming) { newH += 14f; if (newH < entry.CachedHeight) newH = entry.CachedHeight; }
            entry.CachedHeight = newH;
            entry.CachedTextLen = body.Length;
            entry.CachedThinkingLen = thinking.Length;
        }

        private static float DrawEntry(ChatEntry entry, float contentWidth, float y)
        {
            bool isSubagent = !string.IsNullOrEmpty(entry.AgentId);
            bool isThinking = !string.IsNullOrEmpty(entry.ThinkingText);
            string label = entry.IsContext ? "System"
                : entry.Role == ChatRole.User ? "You"
                : isSubagent ? entry.AgentType
                : isThinking ? "AI Thinking" : "AI";

            string body = (entry.Text ?? "").Replace("_", "__");
            string thinking = (entry.ThinkingText ?? "").Replace("_", "__");
            bool streaming = entry.State == ChatState.Streaming;
            string cursor = streaming && Time.realtimeSinceStartup % 1.0f < 0.6f ? "▌" : " ";

            float bodyWidth = contentWidth - 20f;
            float textAreaW = bodyWidth - 12f;
            float entryHeight = entry.CachedHeight;

            Rect bubbleRect = new Rect(2f, y, contentWidth, entryHeight);
            Color bgColor = entry.IsContext ? new Color(0.12f, 0.12f, 0.18f, 1f)
                : entry.Role == ChatRole.User ? UserBgColor
                : entry.State == ChatState.Error ? ErrorBgColor
                : isSubagent ? SubagentBgColor : AiBgColor;
            bgColor.a = _alpha;
            Widgets.DrawBoxSolid(bubbleRect, bgColor);

            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 1 && Mouse.IsOver(bubbleRect))
            {
                GUIUtility.systemCopyBuffer = entry.Text;
                Messages.Message("Copied to clipboard", MessageTypeDefOf.SilentInput, false);
                Event.current.Use();
            }

            Text.Font = GameFont.Small;
            float labelW = Text.CalcSize(label).x + 4f;
            Rect labelRect = new Rect(bubbleRect.x + 6f, bubbleRect.y + 3f, labelW, 20f);
            GUI.color = entry.IsContext ? new Color(0.5f, 0.5f, 0.6f, _alpha)
                : entry.Role == ChatRole.User
                    ? new Color(0.5f, 0.55f, 0.65f, _alpha)
                    : isSubagent
                        ? new Color(0.6f, 0.45f, 0.65f, _alpha)
                        : isThinking
                            ? new Color(0.7f, 0.6f, 0.35f, _alpha)
                            : new Color(0.45f, 0.55f, 0.45f, _alpha);
            Widgets.Label(labelRect, label);

            float curY = labelRect.yMax + 2f;

            if (!string.IsNullOrEmpty(thinking))
            {
                var t = thinking + (isThinking && streaming ? cursor : "");
                float h = Text.CalcHeight(t.StripTags(), textAreaW);
                Rect r = new Rect(bubbleRect.x + 8f, curY, textAreaW, h);
                GUI.color = new Color(0.5f, 0.48f, 0.35f, _alpha);
                Text.Font = GameFont.Small;
                Widgets.Label(r, t);
                curY += h;
            }

            if (!string.IsNullOrEmpty(body) || (!isThinking && streaming))
            {
                var t = body + (!isThinking && streaming ? cursor : "");
                float h = Text.CalcHeight(t.StripTags(), textAreaW);
                Rect r = new Rect(bubbleRect.x + 8f, curY, textAreaW, Mathf.Max(h, 10f));
                GUI.color = entry.IsContext ? new Color(0.55f, 0.55f, 0.6f, _alpha)
                    : new Color(0.85f, 0.85f, 0.85f, _alpha);
                Text.Font = GameFont.Small;
                Widgets.Label(r, t);
            }

            GUI.color = Color.white;
            return entryHeight;
        }

        // ========== 输入行 ==========

        private void DrawInputRow(Rect rect)
        {
            float btnW = 56f;
            float gap = 4f;
            float padX = 2f;

            Rect tfRect = new Rect(rect.x + padX, rect.y + 2f,
                rect.width - btnW - gap - padX * 2, rect.height - 4f);
            GUI.color = Color.white;
            GUI.SetNextControlName("chatInput");
            _inputText = Widgets.TextField(tfRect, _inputText);

            Rect sendRect = new Rect(tfRect.xMax + gap, rect.y + 2f, btnW, rect.height - 4f);
            if (Widgets.ButtonText(sendRect, "Send"))
                TrySendInput();

            GUI.color = Color.white;
        }

        // ========== 底栏 ==========

        private void DrawFooter(Rect rect)
        {
            float btnW = 22f;
            float btnH = rect.height - 4f;
            float y = rect.y + 2f;

            // 连接状态
            float statusX = rect.x + 2f;
            Rect statusRect = new Rect(statusX, y, 90f, btnH);
            Text.Font = GameFont.Tiny;
            GUI.color = BridgeConnected ? new Color(0.4f, 0.55f, 0.4f, _alpha) : new Color(0.7f, 0.35f, 0.35f, _alpha);
            Widgets.Label(statusRect, BridgeConnected ? "● Connected" : "● Disconnected");
            GUI.color = Color.white;

            // 透明度
            float alphaX = statusX + 95f;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.4f, 0.4f, 0.4f, _alpha);
            Widgets.Label(new Rect(alphaX, y, 30f, btnH), "Alpha");
            GUI.color = Color.white;

            Rect alphaMinus = new Rect(alphaX + 24f, y, btnW, btnH);
            if (Widgets.ButtonText(alphaMinus, "-"))
                _alpha = Mathf.Clamp(_alpha - 0.1f, 0.2f, 1f);

            Rect alphaPlus = new Rect(alphaX + 24f + btnW + 2f, y, btnW, btnH);
            if (Widgets.ButtonText(alphaPlus, "+"))
                _alpha = Mathf.Clamp(_alpha + 0.1f, 0.2f, 1f);

            // 右侧按钮
            float rightSide = rect.xMax;
            float actionBtnW = 52f;

            Rect abortRect = new Rect(rightSide - actionBtnW, y, actionBtnW, btnH);
            GUI.color = BridgeConnected ? Color.white : Color.grey;
            if (Widgets.ButtonText(abortRect, "Abort"))
            {
                _ = Bridge?.SendAbort();
            }

            Rect clearRect = new Rect(abortRect.x - 44f - 4f, y, 44f, btnH);
            GUI.color = Color.white;
            if (Widgets.ButtonText(clearRect, "Clear"))
                ChatDisplayState.Clear();

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }
}
