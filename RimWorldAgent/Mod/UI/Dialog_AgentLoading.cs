using UnityEngine;
using Verse;

namespace RimWorldAgent
{
    /// <summary>Agent 初始化加载窗口 — 提示用户正在准备 AI 助手</summary>
    public class Dialog_AgentLoading : Window
    {
        public static volatile string StatusText = "正在准备 AI 助手...";

        public Dialog_AgentLoading()
        {
            doCloseX = false;
            closeOnCancel = false;
            closeOnAccept = false;
            closeOnClickedOutside = false;
            draggable = false;
            resizeable = false;
            forcePause = false;
            layer = WindowLayer.Dialog;
            preventCameraMotion = false;
            doWindowBackground = true;
            drawShadow = true;
        }

        public override Vector2 InitialSize => new Vector2(300f, 100f);

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(
                (UI.screenWidth - InitialSize.x) / 2f,
                (UI.screenHeight - InitialSize.y) / 2f,
                InitialSize.x, InitialSize.y);
            windowRect = windowRect.Rounded();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var text = StatusText;
            Text.Font = GameFont.Medium;
            var size = Text.CalcSize(text);
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(new Rect(0f, 0f, inRect.width, inRect.height), text);
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            Text.Font = GameFont.Small;
        }
    }
}
