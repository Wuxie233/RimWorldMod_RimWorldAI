using UnityEngine;
using Verse;

namespace RimWorldMCP
{
    public class Dialog_BridgeSettings : Window
    {
        private McpModSettings _settings;
        private string _testResult = "";
        private Vector2 _scrollPos;

        public Dialog_BridgeSettings(McpModSettings settings)
        {
            _settings = settings;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = false;
        }

        public override Vector2 InitialSize => new Vector2(500f, 400f);

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("桥接器设置");
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            listing.Label("对接类型");
            var typeLabel = _settings.BridgeType < McpModSettings.BridgeTypeLabels.Length
                ? McpModSettings.BridgeTypeLabels[_settings.BridgeType] : "未知";
            if (listing.ButtonText(typeLabel))
            {
                _settings.BridgeType = (_settings.BridgeType + 1) % McpModSettings.BridgeTypeLabels.Length;
            }
            listing.Gap(12f);

            if (_settings.BridgeType > 0)
            {
                listing.Label("Gateway WebSocket URL");
                listing.Label("示例: ws://localhost:8080/gateway");
                _settings.BridgeUrl = listing.TextEntry(_settings.BridgeUrl);
                listing.Gap(6f);

                listing.Label("Token");
                _settings.BridgeToken = listing.TextEntry(_settings.BridgeToken);
                listing.Gap(6f);

                listing.Label("Password");
                _settings.BridgePassword = listing.TextEntry(_settings.BridgePassword);
                listing.Gap(12f);

                if (listing.ButtonText("测试连接"))
                {
                    TestConnection();
                }

                if (!string.IsNullOrEmpty(_testResult))
                {
                    listing.Label(_testResult);
                }
            }
            else
            {
                listing.Label("选择对接类型以配置桥接器");
            }

            listing.End();
        }

        private async void TestConnection()
        {
            _testResult = "正在连接...";
            bool ok = await McpClient.Connect(_settings.BridgeUrl, _settings.BridgeToken, _settings.BridgePassword);
            _testResult = ok ? "✅ 连接成功" : "❌ 连接失败";
        }
    }
}
