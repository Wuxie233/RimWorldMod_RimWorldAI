using System.Threading.Tasks;

namespace RimWorldMCP
{
    /// <summary>OpenClaw Gateway 连接生命周期管理 — 独立于 MCP Server</summary>
    public static class BridgeLifecycle
    {
        public static async Task StartAsync()
        {
            var settings = RimWorldMCPMod.Instance?.Settings;
            if (settings == null || settings.BridgeType == 0 || string.IsNullOrEmpty(settings.BridgeUrl))
                return;

            await GatewayClient.Connect(settings.BridgeUrl, settings.BridgeToken, settings.BridgePassword);
            if (GatewayClient.IsConnected)
                McpLog.Info($"[bridge] 已连接到 {McpModSettings.BridgeTypeLabels[settings.BridgeType]}: {settings.BridgeUrl}");
        }

        public static void Tick()
        {
            GatewayEventMonitor.Tick();
        }

        public static void Stop()
        {
            GatewayClient.Disconnect();
        }
    }
}
