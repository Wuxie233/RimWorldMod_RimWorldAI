using UnityEngine;
using Verse;

namespace RimWorldAgent
{
    /// <summary>
    /// 游戏内右下角 AI 聊天按钮 — 切换 Dialog_AiChat 窗口。
    /// 绿色虚线框 + "AI" 文字，对话框打开时变为绿色实心。
    /// </summary>
    public class MapComponent_McpUI : MapComponent
    {
        private const float BtnSize = 24f;
        private bool _dialogOpen;

        public MapComponent_McpUI(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (Find.CurrentMap == null) return;

            var rect = new Rect(UI.screenWidth - BtnSize - 190f, UI.screenHeight - BtnSize - 35f,
                BtnSize, BtnSize);

            // 检查对话窗是否已打开
            var dialog = Find.WindowStack?.WindowOfType<Dialog_AiChat>();
            _dialogOpen = dialog != null;

            var bgColor = _dialogOpen
                ? new Color(0.15f, 0.5f, 0.15f, 0.5f)
                : new Color(0.2f, 0.4f, 0.2f, 0.3f);
            Widgets.DrawBoxSolid(rect, bgColor);

            var textColor = _dialogOpen
                ? new Color(0.3f, 1f, 0.3f, 0.9f)
                : new Color(0.4f, 0.7f, 0.4f, 0.6f);
            Text.Font = GameFont.Tiny;
            GUI.color = textColor;
            Widgets.Label(new Rect(rect.x + 3f, rect.y + 4f, BtnSize - 6f, BtnSize - 6f), "AI");
            GUI.color = Color.white;

            // 点击切换
            if (Widgets.ButtonInvisible(rect))
            {
                if (_dialogOpen && dialog != null)
                    dialog.Close();
                else
                    Find.WindowStack?.Add(new Dialog_AiChat());
            }
        }
    }
}
