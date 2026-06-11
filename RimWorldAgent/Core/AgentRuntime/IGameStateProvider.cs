using System.Threading.Tasks;

namespace RimWorldAgent.Core.AgentRuntime
{
    /// <summary>游戏状态提供者。调用方通过 SyncGameStatusAsync() 刷新缓存后读取各属性。</summary>
    public interface IGameStateProvider
    {
        int GameTick { get; }
        int GameDay { get; }
        int GameHour { get; }
        string SeasonName { get; }
        int DayOfQuadrum { get; }
        string SpeedLabel { get; }
        bool IsPaused { get; }

        bool ShouldMorningReport();
        void MarkMorningReportSent();
        bool ShouldWake(int intervalGameHours);
        void Reset();

        /// <summary>同步游戏状态到内部缓存：tick / paused。Direct 读 TickManager，Remote 调 MCP。</summary>
        Task SyncGameStatusAsync();
    }

    public abstract class GameStateBase : IGameStateProvider
    {
        protected int _gameTick;
        protected int _lastWakeTick;
        protected int _lastMorningDay = -1;
        protected string _seasonName = "";
        protected int _dayOfQuadrum;
        protected string _speedLabel = "未知";

        public int GameTick => _gameTick;
        public int GameDay => _gameTick / 60000;
        public int GameHour => (_gameTick / 2500) % 24;
        public string SeasonName => string.IsNullOrEmpty(_seasonName) ? GetFallbackSeasonName(GameDay) : _seasonName;
        public int DayOfQuadrum => _dayOfQuadrum > 0 ? _dayOfQuadrum : GetFallbackDayOfQuadrum(GameDay);
        public string SpeedLabel => string.IsNullOrEmpty(_speedLabel) ? (IsPaused ? "已暂停" : "未知") : _speedLabel;
        public abstract bool IsPaused { get; }

        public bool ShouldMorningReport()
            => GameHour >= 6 && GameDay > _lastMorningDay;

        public void MarkMorningReportSent()
            => _lastMorningDay = GameDay;

        public bool ShouldWake(int intervalGameHours)
        {
            int interval = intervalGameHours * 2500;
            if (_gameTick - _lastWakeTick >= interval)
            {
                _lastWakeTick = _gameTick;
                return true;
            }
            return false;
        }

        public virtual void Reset()
        {
            _lastWakeTick = 0;
            _lastMorningDay = -1;
            _seasonName = "";
            _dayOfQuadrum = 0;
            _speedLabel = "未知";
        }

        public virtual Task SyncGameStatusAsync() => Task.CompletedTask;

        protected void UpdateLocalDateFallback()
        {
            var day = GameDay;
            _seasonName = GetFallbackSeasonName(day);
            _dayOfQuadrum = GetFallbackDayOfQuadrum(day);
        }

        protected static string GetFallbackSeasonName(int gameDay)
        {
            var quadrum = ((gameDay % 60) + 60) % 60 / 15;
            return quadrum switch
            {
                0 => "Spring",
                1 => "Summer",
                2 => "Fall",
                3 => "Winter",
                _ => "未知季节"
            };
        }

        protected static int GetFallbackDayOfQuadrum(int gameDay)
            => ((gameDay % 15) + 15) % 15 + 1;
    }
}
