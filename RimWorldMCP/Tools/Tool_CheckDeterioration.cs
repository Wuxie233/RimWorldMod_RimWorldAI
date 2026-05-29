using System.Text.Json;
using System.Threading.Tasks;
using Verse;

namespace RimWorldMCP.Tools
{
    public class Tool_CheckDeterioration : ITool
    {
        public string Name => "check_deterioration";
        public string Description => "扫描地图检查物品腐坏和露天耐久降低，跨阈值(20%/50%/70%)时返回警告。可周期调用，内置 6000 tick 扫描间隔和 15000 tick 通知冷却，频繁调用无副作用。";

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            required = new string[] { }
        });

        public Task<ToolResult> ExecuteAsync(JsonElement? args) => Task.FromResult(Execute(args));

        private static ToolResult Execute(JsonElement? args)
        {
            var map = Find.CurrentMap;
            if (map == null)
                return ToolResult.Success("无活跃地图，无法检查物品腐坏。");

            var text = DeteriorationTracker.CheckAndNotify(map);
            return ToolResult.Success(
                text ?? "当前未检测到新的物品腐坏或露天掉耐久。\n（提示：扫描有内置冷却，频繁调用不会立即重新扫描，需要等待约 2.4 游戏小时后才会再次检测。）");
        }

        public (int minX, int minZ, int maxX, int maxZ)? GetTargetRange(JsonElement? args) => null;
    }
}
