using System.Threading.Tasks;
using RimWorld;
using RimWorldAgent.Core.AgentRuntime;
using Verse;

namespace RimWorldAgent
{
    /// <summary>MOD 模式 — 直接从 Find.TickManager 读取，无需 MCP 往返</summary>
    public class DirectGameStateProvider : GameStateBase
    {
        public override bool IsPaused
        {
            get
            {
                var tm = Find.TickManager;
                return tm != null && tm.Paused;
            }
        }

        public override Task SyncGameStatusAsync()
        {
            var tm = Find.TickManager;
            if (tm != null)
            {
                _gameTick = tm.TicksGame;
                _speedLabel = BuildSpeedLabel(tm);
            }
            UpdateLocalDate();
            return Task.CompletedTask;
        }

        private void UpdateLocalDate()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                UpdateLocalDateFallback();
                return;
            }

            _seasonName = GenLocalDate.Season(map).ToString();
            _dayOfQuadrum = GenLocalDate.DayOfQuadrum(map);
        }

        private static string BuildSpeedLabel(TickManager tickManager)
        {
            if (tickManager.Paused) return "已暂停";
            return tickManager.CurTimeSpeed switch
            {
                TimeSpeed.Normal => "1 倍速",
                TimeSpeed.Fast => "2 倍速",
                TimeSpeed.Superfast => "3 倍速",
                TimeSpeed.Ultrafast => "最快",
                _ => tickManager.CurTimeSpeed.ToString()
            };
        }
    }
}
